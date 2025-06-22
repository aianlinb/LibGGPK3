package ggpk

import (
	"encoding/binary"
	"fmt"
	"io"
	"os"

	"strings"

	"github.com/pierrec/lz4/v4"
	encunicode "golang.org/x/text/encoding/unicode"
	"golang.org/x/text/encoding/unicode/utf32" // Specific import for UTF-32
	"golang.org/x/text/transform"
)

// GGPKFile represents an opened GGPK file.
type GGPKFile struct {
	File            *os.File
	Header          GGPKRecord
	Root            *DirectoryRecord // Parsed root directory
	recordCache     map[int64]interface{}
	stringReadBuf   []byte // Reusable buffer for string reading
	utf16LEDecoder  transform.Transformer
	utf32LEDecoder  transform.Transformer
}

// Open opens a GGPK file, reads its header, and returns a GGPKFile struct.
func Open(filepath string) (*GGPKFile, error) {
	f, err := os.Open(filepath)
	if err != nil {
		return nil, fmt.Errorf("failed to open file %s: %w", filepath, err)
	}

	ggpkFile := &GGPKFile{
		File:           f,
		recordCache:    make(map[int64]interface{}),
		stringReadBuf:  make([]byte, 1024), // Initial size, can grow
		utf16LEDecoder: encunicode.UTF16(encunicode.LittleEndian, encunicode.IgnoreBOM).NewDecoder(),
		// Try using constants from the utf32 package itself, assuming they are re-exported or defined compatibly.
		utf32LEDecoder: utf32.UTF32(utf32.LittleEndian, utf32.IgnoreBOM).NewDecoder(),
	}

	// The GGPKRecord is always at offset 0
	header, err := ggpkFile.parseGGPKRecordBody(0)
	if err != nil {
		f.Close()
		return nil, fmt.Errorf("failed to parse GGPK header: %w", err)
	}
	ggpkFile.Header = *header
	ggpkFile.recordCache[0] = header


	// Basic validation
	if ggpkFile.Header.Tag != GGPKRecordTag {
		f.Close()
		return nil, fmt.Errorf("invalid GGPK file: magic tag not found. Expected %X, got %X", GGPKRecordTag, ggpkFile.Header.Tag)
	}

	// Parse the root directory
	// The root directory name is empty.
	root, err := ggpkFile.ReadDirectoryRecordAt(ggpkFile.Header.RootDirectoryOffset, nil, "")
	if err != nil {
		f.Close()
		return nil, fmt.Errorf("failed to parse root directory: %w", err)
	}
	ggpkFile.Root = root


	return ggpkFile, nil
}

// Close closes the underlying file handle.
func (gf *GGPKFile) Close() error {
	if gf.File != nil {
		return gf.File.Close()
	}
	return nil
}

// readRecordHeader reads the common length and tag from a record at the given offset.
// It returns the record's total length, its tag, and any error encountered.
// The file's seek pointer is left at the start of the record's body (after tag).
func (gf *GGPKFile) readRecordHeaderAndSeek(offset int64) (length int32, tag uint32, err error) {
	if _, err = gf.File.Seek(offset, io.SeekStart); err != nil {
		return 0, 0, fmt.Errorf("seek to offset %d failed: %w", offset, err)
	}

	if err = binary.Read(gf.File, GGPKEndian, &length); err != nil {
		return 0, 0, fmt.Errorf("failed to read record length at offset %d: %w", offset, err)
	}

	if err = binary.Read(gf.File, GGPKEndian, &tag); err != nil {
		return 0, 0, fmt.Errorf("failed to read record tag at offset %d: %w", offset, err)
	}
	return length, tag, nil
}

// parseGGPKRecordBody reads and parses the GGPKRecord from the given offset.
// The GGPKRecord is typically at offset 0.
func (gf *GGPKFile) parseGGPKRecordBody(offset int64) (*GGPKRecord, error) {
	recordLength, tag, err := gf.readRecordHeaderAndSeek(offset)
	if err != nil {
		return nil, err
	}

	if tag != GGPKRecordTag {
		return nil, fmt.Errorf("expected GGPKRecord tag %X at offset %d, but got %X", GGPKRecordTag, offset, tag)
	}

	record := &GGPKRecord{
		BaseRecord: BaseRecord{
			Offset: offset,
			Length: recordLength,
			Tag:    tag,
		},
	}

	// Read the rest of the GGPKRecord fields
	if err := binary.Read(gf.File, GGPKEndian, &record.Version); err != nil {
		return nil, fmt.Errorf("failed to read GGPKRecord Version: %w", err)
	}
	if err := binary.Read(gf.File, GGPKEndian, &record.RootDirectoryOffset); err != nil {
		return nil, fmt.Errorf("failed to read GGPKRecord RootDirectoryOffset: %w", err)
	}
	if err := binary.Read(gf.File, GGPKEndian, &record.FirstFreeOffset); err != nil {
		return nil, fmt.Errorf("failed to read GGPKRecord FirstFreeOffset: %w", err)
	}

	return record, nil
}

// readUTF16String reads a null-terminated UTF-16LE string of nameLength characters.
func (gf *GGPKFile) readUTF16String(nameLengthChars uint32) (string, error) {
	if nameLengthChars == 0 {
		return "", nil
	}
	numBytes := int(nameLengthChars * 2) // UTF-16 uses 2 bytes per character
	if cap(gf.stringReadBuf) < numBytes {
		gf.stringReadBuf = make([]byte, numBytes)
	} else {
		gf.stringReadBuf = gf.stringReadBuf[:numBytes]
	}

	if _, err := io.ReadFull(gf.File, gf.stringReadBuf); err != nil {
		return "", fmt.Errorf("failed to read UTF-16 string bytes: %w", err)
	}

	// UTF-16LE: remove null terminator before decoding if it's included in nameLengthChars
	// The C# code does `nameLength = s.Read<int>() - 1;` implying null term is counted.
	// And then `s.Seek(sizeof(char), SeekOrigin.Current);` meaning it reads string then skips null.
	// Here, nameLengthChars *already* includes the null terminator.
	// So we read (nameLengthChars * 2) bytes. The actual string is (nameLengthChars-1) chars.

	actualStringBytes := gf.stringReadBuf
	if nameLengthChars > 0 { // Ensure there's a null terminator to slice off
		actualStringBytes = gf.stringReadBuf[:(nameLengthChars-1)*2]
	}


	utf8Bytes, _, err := transform.Bytes(gf.utf16LEDecoder, actualStringBytes)
	if err != nil {
		return "", fmt.Errorf("failed to decode UTF-16LE string: %w", err)
	}
	return string(utf8Bytes), nil
}

// readUTF32String reads a null-terminated UTF-32LE string of nameLength characters.
func (gf *GGPKFile) readUTF32String(nameLengthChars uint32) (string, error) {
	if nameLengthChars == 0 {
		return "", nil
	}
	numBytes := int(nameLengthChars * 4) // UTF-32 uses 4 bytes per character
	if cap(gf.stringReadBuf) < numBytes {
		gf.stringReadBuf = make([]byte, numBytes)
	} else {
		gf.stringReadBuf = gf.stringReadBuf[:numBytes]
	}

	if _, err := io.ReadFull(gf.File, gf.stringReadBuf); err != nil {
		return "", fmt.Errorf("failed to read UTF-32 string bytes: %w", err)
	}

	actualStringBytes := gf.stringReadBuf
	if nameLengthChars > 0 {
		actualStringBytes = gf.stringReadBuf[:(nameLengthChars-1)*4]
	}

	utf8Bytes, _, err := transform.Bytes(gf.utf32LEDecoder, actualStringBytes)
	if err != nil {
		return "", fmt.Errorf("failed to decode UTF-32LE string: %w", err)
	}
	return string(utf8Bytes), nil
}

func (gf *GGPKFile) parseFileRecordBody(offset int64, baseRecord BaseRecord) (*FileRecord, error) {
	record := &FileRecord{BaseRecord: baseRecord}

	if err := binary.Read(gf.File, GGPKEndian, &record.NameLength); err != nil {
		return nil, fmt.Errorf("failed to read FileRecord NameLength: %w", err)
	}
	if _, err := io.ReadFull(gf.File, record.Hash[:]); err != nil {
		return nil, fmt.Errorf("failed to read FileRecord Hash: %w", err)
	}

	var nameString string
	var err error
	if gf.Header.Version == 4 { // Mac version uses UTF-32
		nameString, err = gf.readUTF32String(record.NameLength)
	} else { // PC version uses UTF-16
		nameString, err = gf.readUTF16String(record.NameLength)
	}
	if err != nil {
		return nil, fmt.Errorf("failed to read FileRecord Name: %w", err)
	}
	record.Name = nameString

	// Calculate DataOffset and DataLength
	// Current position is after BaseRecord (Length, Tag), NameLength, Hash, and Name string (including null terminator)
	nameBytesLength := record.NameLength
	if gf.Header.Version == 4 {
		nameBytesLength *= 4 // UTF-32
	} else {
		nameBytesLength *= 2 // UTF-16
	}

	headerSizeWithoutData := RecordHeaderSize + 4 /*NameLength*/ + HashSize + int32(nameBytesLength)
	record.DataLength = record.Length - headerSizeWithoutData
	record.DataOffset = record.Offset + int64(headerSizeWithoutData)

	// Note: We don't read the file data here, only its metadata.
	// The file pointer should be at the end of the record after this.
	// Seek to end of record to ensure next read starts correctly.
	expectedEndOffset := record.Offset + int64(record.Length)
	if _, err := gf.File.Seek(expectedEndOffset, io.SeekStart); err != nil {
		return nil, fmt.Errorf("failed to seek to end of FileRecord at %d: %w", expectedEndOffset, err)
	}

	return record, nil
}

func (gf *GGPKFile) parseDirectoryRecordBody(offset int64, baseRecord BaseRecord, assignedNameIfRoot string) (*DirectoryRecord, error) {
	record := &DirectoryRecord{BaseRecord: baseRecord}
	record.childRecordsDirty = true // Children are not parsed yet

	if err := binary.Read(gf.File, GGPKEndian, &record.NameLength); err != nil {
		return nil, fmt.Errorf("failed to read DirectoryRecord NameLength: %w", err)
	}
	if err := binary.Read(gf.File, GGPKEndian, &record.EntryCount); err != nil {
		return nil, fmt.Errorf("failed to read DirectoryRecord EntryCount: %w", err)
	}
	if _, err := io.ReadFull(gf.File, record.Hash[:]); err != nil {
		return nil, fmt.Errorf("failed to read DirectoryRecord Hash: %w", err)
	}

	if assignedNameIfRoot != "" && record.NameLength == 0 { // Special case for root dir which has no name in record
	    record.Name = assignedNameIfRoot
	} else {
		var nameString string
		var err error
		if gf.Header.Version == 4 { // Mac version uses UTF-32
			nameString, err = gf.readUTF32String(record.NameLength)
		} else { // PC version uses UTF-16
			nameString, err = gf.readUTF16String(record.NameLength)
		}
		if err != nil {
			return nil, fmt.Errorf("failed to read DirectoryRecord Name: %w", err)
		}
		record.Name = nameString
	}


	record.Entries = make([]DirectoryEntry, record.EntryCount)
	for i := uint32(0); i < record.EntryCount; i++ {
		if err := binary.Read(gf.File, GGPKEndian, &record.Entries[i].NameHash); err != nil {
			return nil, fmt.Errorf("failed to read DirectoryEntry NameHash for entry %d: %w", i, err)
		}
		if err := binary.Read(gf.File, GGPKEndian, &record.Entries[i].Offset); err != nil {
			return nil, fmt.Errorf("failed to read DirectoryEntry Offset for entry %d: %w", i, err)
		}
	}
	record.Children = make([]TreeNode, record.EntryCount) // Initialize with nil

	return record, nil
}

func (gf *GGPKFile) parseFreeRecordBody(offset int64, baseRecord BaseRecord) (*FreeRecord, error) {
	record := &FreeRecord{BaseRecord: baseRecord}
	if err := binary.Read(gf.File, GGPKEndian, &record.NextFreeOffset); err != nil {
		return nil, fmt.Errorf("failed to read FreeRecord NextFreeOffset: %w", err)
	}
	// The rest of the record is free space, seek to the end of it
	expectedEndOffset := record.Offset + int64(record.Length)
	if _, currentPos, err := gf.currentOffset(); err != nil {
		return nil, fmt.Errorf("failed to get current offset for FreeRecord end seek: %w", err)
	} else if currentPos != expectedEndOffset { // If not already there (e.g. NextFreeOffset was last field)
		if _, err := gf.File.Seek(expectedEndOffset, io.SeekStart); err != nil {
			return nil, fmt.Errorf("failed to seek to end of FreeRecord at %d: %w", expectedEndOffset, err)
		}
	}
	return record, nil
}

func (gf *GGPKFile) currentOffset() (int64, int64, error) {
	pos, err := gf.File.Seek(0, io.SeekCurrent)
	return pos, pos, err
}


// ReadRecordAt attempts to read and identify a record at a given offset.
// It uses a cache to avoid re-parsing known records.
func (gf *GGPKFile) ReadRecordAt(offset int64) (interface{}, error) {
	if cachedRecord, found := gf.recordCache[offset]; found {
		return cachedRecord, nil
	}

	length, tag, err := gf.readRecordHeaderAndSeek(offset)
	if err != nil {
		return nil, fmt.Errorf("failed to read record header at offset %d: %w", offset, err)
	}

	baseRec := BaseRecord{
		Offset: offset,
		Length: length,
		Tag:    tag,
	}

	var parsedRecord interface{}
	switch tag {
	case GGPKRecordTag:
		// This case should ideally be handled by initial Open, but good for generic reading
		parsedRecord, err = gf.parseGGPKRecordBody(offset)
	case PDirRecordTag:
		// Name is not known here, will be empty or set by caller if root
		parsedRecord, err = gf.parseDirectoryRecordBody(offset, baseRec, "")
	case FileRecordTag:
		parsedRecord, err = gf.parseFileRecordBody(offset, baseRec)
	case FreeRecordTag:
		parsedRecord, err = gf.parseFreeRecordBody(offset, baseRec)
	default:
		// Seek to end of unknown record to allow further parsing
		_, seekErr := gf.File.Seek(offset+int64(length), io.SeekStart)
		if seekErr != nil {
			return nil, fmt.Errorf("unknown record tag %X at offset %d and failed to seek past it: %w", tag, offset, seekErr)
		}
		return nil, fmt.Errorf("unknown record tag %X at offset %d (skipped)", tag, offset)
	}

	if err != nil {
		return nil, err
	}

	gf.recordCache[offset] = parsedRecord
	return parsedRecord, nil
}


// ReadDirectoryRecordAt is a specialized version of ReadRecordAt for directories.
// It sets the parent and, if known (e.g. for root), the name.
func (gf *GGPKFile) ReadDirectoryRecordAt(offset int64, parent *DirectoryRecord, assignedName string) (*DirectoryRecord, error) {
    if offset == 0 { // Safety check, PDIR shouldn't be at 0 normally
        return nil, fmt.Errorf("invalid directory offset 0")
    }
	record, err := gf.ReadRecordAt(offset)
	if err != nil {
		return nil, fmt.Errorf("failed to read record for directory at offset %d: %w", offset, err)
	}
	dirRecord, ok := record.(*DirectoryRecord)
	if !ok {
		return nil, fmt.Errorf("expected DirectoryRecord at offset %d, but got %T", offset, record)
	}

	if assignedName != "" && dirRecord.Name == "" { // For root, name is not in record
	    dirRecord.Name = assignedName
	}
	dirRecord.SetParent(parent)
	return dirRecord, nil
}

// ReadFileRecordAt is a specialized version for files.
func (gf *GGPKFile) ReadFileRecordAt(offset int64, parent *DirectoryRecord) (*FileRecord, error) {
    if offset == 0 { // Safety check, FILE shouldn't be at 0
        return nil, fmt.Errorf("invalid file offset 0")
    }
	record, err := gf.ReadRecordAt(offset)
	if err != nil {
		return nil, fmt.Errorf("failed to read record for file at offset %d: %w", offset, err)
	}
	fileRecord, ok := record.(*FileRecord)
	if !ok {
		return nil, fmt.Errorf("expected FileRecord at offset %d, but got %T", offset, record)
	}
	fileRecord.SetParent(parent)
	return fileRecord, nil
}


// GetChildren populates the Children slice of a DirectoryRecord by parsing its entries.
func (dr *DirectoryRecord) GetChildren(gf *GGPKFile) ([]TreeNode, error) {
	if !dr.childRecordsDirty && dr.Children != nil && len(dr.Children) == int(dr.EntryCount) {
		// Assuming if not dirty and children slice matches entry count, it's populated.
		// A more robust check might be needed if partial population is possible.
		return dr.Children, nil
	}

	dr.Children = make([]TreeNode, dr.EntryCount)
	for i, entry := range dr.Entries {
		// Determine if it's a directory or file by looking at the tag of the record at entry.Offset
		// This requires reading the header of the child record.
		_, tag, err := gf.readRecordHeaderAndSeek(entry.Offset)
		if err != nil {
			return nil, fmt.Errorf("error reading child record header for entry %s (hash %X) at offset %d: %w", dr.Name, entry.NameHash, entry.Offset, err)
		}

		var childNode TreeNode
		switch tag {
		case PDirRecordTag:
			childNode, err = gf.ReadDirectoryRecordAt(entry.Offset, dr, "") // Name will be parsed from record
		case FileRecordTag:
			childNode, err = gf.ReadFileRecordAt(entry.Offset, dr)
		default:
			// This case should ideally not happen if GGPK is well-formed and entry points to valid FILE/PDIR
			// Or it could be a FreeRecord, which we might want to handle or log
			gf.File.Seek(entry.Offset+RecordHeaderSize, io.SeekStart) // Reset seek to after header for next potential read
			return nil, fmt.Errorf("child entry %s (hash %X) at offset %d has unexpected tag %X", dr.Name, entry.NameHash, entry.Offset, tag)
		}

		if err != nil {
			return nil, fmt.Errorf("error parsing child node for entry %s (hash %X) at offset %d: %w", dr.Name, entry.NameHash, entry.Offset, err)
		}
		dr.Children[i] = childNode
	}
	dr.childRecordsDirty = false
	return dr.Children, nil
}

// ReadFileData reads the data for a given FileRecord.
// It attempts to handle LZ4 decompression if the data appears to be prefixed
// with an uncompressed size (a common GGPK convention for compressed files).
func (gf *GGPKFile) ReadFileData(fileRecord *FileRecord) ([]byte, error) {
	if fileRecord == nil {
		return nil, fmt.Errorf("FileRecord is nil")
	}
	if fileRecord.DataLength == 0 {
		return []byte{}, nil // Empty file
	}
	if fileRecord.DataLength < 0 {
		return nil, fmt.Errorf("FileRecord has negative DataLength: %d for file %s", fileRecord.DataLength, fileRecord.Name)
	}

	rawData := make([]byte, fileRecord.DataLength)
	if _, err := gf.File.Seek(fileRecord.DataOffset, io.SeekStart); err != nil {
		return nil, fmt.Errorf("failed to seek to data offset %d for file %s: %w", fileRecord.DataOffset, fileRecord.Name, err)
	}

	if _, err := io.ReadFull(gf.File, rawData); err != nil {
		return nil, fmt.Errorf("failed to read raw data for file %s: %w", fileRecord.Name, err)
	}

	// Attempt LZ4 decompression if data length is sufficient for the prefix
	// and the uncompressed size makes sense.
	// This is a heuristic. Some files might not be compressed or use other schemes.
	if fileRecord.DataLength >= 4 {
		uncompressedSize := GGPKEndian.Uint32(rawData[0:4])
		compressedData := rawData[4:]

		// Heuristic: If uncompressedSize is vastly different from DataLength-4,
		// or if uncompressedSize is 0 but DataLength > 4, it might not be this LZ4 format.
		// Or if uncompressed size is same as compressed size, it's effectively uncompressed.
		if uncompressedSize == uint32(len(compressedData)) {
			// Data is "compressed" but output size is same as input size (minus prefix).
			// This means it was stored uncompressed with the prefix.
			return compressedData, nil
		}

		// Avoid excessively large allocations if uncompressedSize is unreasonable.
		// Max typical file size in GGPK, e.g. 500MB. If bigger, likely not this format or corrupt.
		// This limit should be configurable or based on more specific file type knowledge.
		const reasonableMaxSize = 500 * 1024 * 1024
		if uncompressedSize > 0 && uncompressedSize <= reasonableMaxSize {
			// Check if the file name suggests it shouldn't be decompressed (e.g. specific text files)
			// For now, we'll try to decompress if the prefix looks valid.
			// A more robust system might check file extensions.
			// Example: !strings.HasSuffix(fileRecord.Name, ".txt") && !strings.HasSuffix(fileRecord.Name, ".lua")

			decompressedData := make([]byte, uncompressedSize)
			n, err := lz4.UncompressBlock(compressedData, decompressedData)
			if err == nil && n == int(uncompressedSize) {
				return decompressedData, nil // Successfully decompressed
			}
			// If decompression fails, or size doesn't match, fall through to return rawData.
			// This could happen if the file isn't actually LZ4 compressed in this way,
			// or if it's a different compression or just raw data that happens to start with bytes
			// that look like a size prefix.
		}
	}

	// If not decompressed (or decompression failed, or not applicable), return the raw data.
	// This might be the correct uncompressed data for some files, or still compressed for others
	// if our LZ4 heuristic didn't apply/work.
	return rawData, nil
}

// FindChildByName searches for a direct child (file or directory) by its name.
// It will load children of the directory record if they haven't been loaded yet.
func (dr *DirectoryRecord) FindChildByName(name string, gf *GGPKFile) (TreeNode, error) {
	if dr == nil {
		return nil, fmt.Errorf("cannot find child in a nil directory record")
	}
	children, err := dr.GetChildren(gf)
	if err != nil {
		return nil, fmt.Errorf("failed to get children for directory %s: %w", dr.GetPath(), err)
	}

	for _, child := range children {
		if child != nil && child.GetName() == name {
			return child, nil
		}
	}
	return nil, fmt.Errorf("child node '%s' not found in directory '%s'", name, dr.GetPath())
}

// GetNodeByPath traverses the GGPK structure from the root to find a node (file or directory)
// at the given path. Path components should be separated by '/'.
func (gf *GGPKFile) GetNodeByPath(path string) (TreeNode, error) {
	if path == "" || path == "/" {
		return gf.Root, nil
	}

	parts := strings.Split(strings.Trim(path, "/"), "/")
	currentNode := TreeNode(gf.Root)

	for i, part := range parts {
		if part == "" { // Should not happen with strings.Split on a trimmed path
			continue
		}

		dirNode, ok := currentNode.(*DirectoryRecord)
		if !ok {
			return nil, fmt.Errorf("path component '%s' encountered a file node, expected a directory at '%s'", part, strings.Join(parts[:i], "/"))
		}

		childNode, err := dirNode.FindChildByName(part, gf)
		if err != nil {
			return nil, fmt.Errorf("failed to find path component '%s' in directory '%s': %w", part, dirNode.GetPath(), err)
		}
		currentNode = childNode
	}

	return currentNode, nil
}
