package ggpk

import (
	"bytes"
	"encoding/binary"
	"os"
	"testing"

	encunicode "golang.org/x/text/encoding/unicode"
	utf32encoding "golang.org/x/text/encoding/unicode/utf32" // Specific for UTF32 function
	"github.com/pierrec/lz4/v4"
	"golang.org/x/text/transform"
)

// Helper function to create a temporary file with given bytes
func createTempFile(t *testing.T, content []byte) (string, func()) {
	t.Helper()
	tmpFile, err := os.CreateTemp(t.TempDir(), "test_ggpk_*.ggpk")
	if err != nil {
		t.Fatalf("Failed to create temp file: %v", err)
	}
	if _, err := tmpFile.Write(content); err != nil {
		tmpFile.Close()
		t.Fatalf("Failed to write to temp file: %v", err)
	}
	fileName := tmpFile.Name()
	if err := tmpFile.Close(); err != nil {
		t.Fatalf("Failed to close temp file: %v", err)
	}
	return fileName, func() { os.Remove(fileName) }
}

// TestParseGGPKRecordBody tests the parsing of the main GGPK header record.
func TestParseGGPKRecordBody(t *testing.T) {
	// Manually construct GGPKRecord body bytes (Version, RootOffset, FreeOffset)
	// Length: 28 (standard for GGPK record: 8 for base + 4 for version + 8 for root + 8 for free)
	// Tag: GGPKRecordTag
	// Version: 3
	// RootDirectoryOffset: 0x1000
	// FirstFreeOffset: 0x2000

	var ggpkRecordBody bytes.Buffer
	binary.Write(&ggpkRecordBody, GGPKEndian, uint32(3))          // Version
	binary.Write(&ggpkRecordBody, GGPKEndian, int64(0x1000))      // RootDirectoryOffset
	binary.Write(&ggpkRecordBody, GGPKEndian, int64(0x2000))      // FirstFreeOffset

	// Construct the full record with header
	var fullRecord bytes.Buffer
	binary.Write(&fullRecord, GGPKEndian, int32(RecordHeaderSize + ggpkRecordBody.Len())) // Length
	binary.Write(&fullRecord, GGPKEndian, uint32(GGPKRecordTag))                         // Tag
	fullRecord.Write(ggpkRecordBody.Bytes())

	filePath, cleanup := createTempFile(t, fullRecord.Bytes())
	defer cleanup()

	f, err := os.Open(filePath)
	if err != nil {
		t.Fatalf("Failed to open test file: %v", err)
	}
	defer f.Close()

	gf := &GGPKFile{File: f} // Simplified GGPKFile for this test

	parsedRecord, err := gf.parseGGPKRecordBody(0)
	if err != nil {
		t.Fatalf("parseGGPKRecordBody failed: %v", err)
	}

	if parsedRecord.Tag != GGPKRecordTag {
		t.Errorf("Expected tag %X, got %X", GGPKRecordTag, parsedRecord.Tag)
	}
	if parsedRecord.Version != 3 {
		t.Errorf("Expected version 3, got %d", parsedRecord.Version)
	}
	if parsedRecord.RootDirectoryOffset != 0x1000 {
		t.Errorf("Expected RootDirectoryOffset 0x1000, got %X", parsedRecord.RootDirectoryOffset)
	}
	if parsedRecord.FirstFreeOffset != 0x2000 {
		t.Errorf("Expected FirstFreeOffset 0x2000, got %X", parsedRecord.FirstFreeOffset)
	}
}

// Helper to create FileRecord bytes for testing parseFileRecordBody
func createFileRecordBytes(t *testing.T, name string, ggpkVersion uint32, fileData []byte) []byte {
	t.Helper()
	var recordBody bytes.Buffer

	// NameLength (including null terminator)
	nameLenChars := uint32(len([]rune(name)) + 1)
	binary.Write(&recordBody, GGPKEndian, nameLenChars)

	// Hash (dummy hash)
	var hash [HashSize]byte
	for i := 0; i < HashSize; i++ { hash[i] = byte(i) }
	recordBody.Write(hash[:])

	// Name (UTF-16 or UTF-32)
	if ggpkVersion == 4 { // UTF-32
		// UTF32 function and constants from utf32encoding package
		utf32Encoder := utf32encoding.UTF32(utf32encoding.LittleEndian, utf32encoding.IgnoreBOM).NewEncoder()
		utf32Bytes, _, _ := transform.Bytes(utf32Encoder, []byte(name))
		recordBody.Write(utf32Bytes)
		binary.Write(&recordBody, GGPKEndian, uint32(0)) // Null terminator for UTF-32
	} else { // UTF-16
		// UTF16 function and constants from encunicode (base unicode package)
		utf16Encoder := encunicode.UTF16(encunicode.LittleEndian, encunicode.IgnoreBOM).NewEncoder()
		utf16Bytes, _, _ := transform.Bytes(utf16Encoder, []byte(name))
		recordBody.Write(utf16Bytes)
		binary.Write(&recordBody, GGPKEndian, uint16(0)) // Null terminator for UTF-16
	}

	// File Data itself is not part of this specific record block being parsed by parseFileRecordBody directly from stream before data section
	// but its length contributes to total record length

	var fullRecord bytes.Buffer
	recordLength := RecordHeaderSize + int32(recordBody.Len()) + int32(len(fileData))
	binary.Write(&fullRecord, GGPKEndian, recordLength)
	binary.Write(&fullRecord, GGPKEndian, uint32(FileRecordTag))
	fullRecord.Write(recordBody.Bytes())
	fullRecord.Write(fileData) // Actual file data appended

	return fullRecord.Bytes()
}

func TestParseFileRecordBody(t *testing.T) {
	testCases := []struct {
		name         string
		ggpkVersion  uint32
		fileName     string
		fileData     []byte
		expectedName string
	}{
		{"UTF16 Name", 3, "testfile.txt", []byte("hello"), "testfile.txt"},
		{"UTF32 Name", 4, "你好世界.dat", []byte("world"), "你好世界.dat"},
		{"Empty File", 3, "empty.dat", []byte{}, "empty.dat"},
	}

	for _, tc := range testCases {
		t.Run(tc.name, func(t *testing.T) {
			fileRecordBytes := createFileRecordBytes(t, tc.fileName, tc.ggpkVersion, tc.fileData)

			filePath, cleanup := createTempFile(t, fileRecordBytes)
			defer cleanup()

			f, err := os.Open(filePath)
			if err != nil {
				t.Fatalf("Failed to open test file: %v", err)
			}
			defer f.Close()

			// Simulate GGPKFile context
			gf := &GGPKFile{
				File: f,
				Header: GGPKRecord{Version: tc.ggpkVersion}, // Crucial for string decoding
				utf16LEDecoder: encunicode.UTF16(encunicode.LittleEndian, encunicode.IgnoreBOM).NewDecoder(),
				utf32LEDecoder: utf32encoding.UTF32(utf32encoding.LittleEndian, utf32encoding.IgnoreBOM).NewDecoder(),
				stringReadBuf:  make([]byte, 1024),
			}

			// Read header to position stream correctly for parseFileRecordBody
			recordLenFromFile, tagFromFile, err := gf.readRecordHeaderAndSeek(0)
			if err != nil {
				t.Fatalf("readRecordHeaderAndSeek failed: %v", err)
			}
			if tagFromFile != FileRecordTag {
				t.Fatalf("Expected FILE tag, got %X", tagFromFile)
			}

			baseRec := BaseRecord{Offset: 0, Length: recordLenFromFile, Tag: tagFromFile}
			parsedRecord, err := gf.parseFileRecordBody(0, baseRec)
			if err != nil {
				t.Fatalf("parseFileRecordBody failed for %s: %v", tc.fileName, err)
			}

			if parsedRecord.Name != tc.expectedName {
				t.Errorf("Expected file name '%s', got '%s'", tc.expectedName, parsedRecord.Name)
			}
			if parsedRecord.DataLength != int32(len(tc.fileData)) {
				t.Errorf("Expected data length %d, got %d", len(tc.fileData), parsedRecord.DataLength)
			}

			// Verify DataOffset (relative to start of this specific temp file)
			// BaseRecordHeader + NameLengthField + Hash + NameBytesWithNullTerm
			nameLenChars := uint32(len([]rune(tc.fileName)) + 1)
			nameBytes := nameLenChars
			if tc.ggpkVersion == 4 { nameBytes *= 4 } else { nameBytes *= 2 }
			expectedDataOffset := int64(RecordHeaderSize + 4 + HashSize + int32(nameBytes))

			if parsedRecord.DataOffset != expectedDataOffset {
				t.Errorf("Expected data offset %d, got %d", expectedDataOffset, parsedRecord.DataOffset)
			}
		})
	}
}


// Minimal GGPK for integration testing Open, GetNodeByPath, ReadFileData
// Structure:
// 1. GGPK Record (Offset 0) -> Points to Root Dir
// 2. Root Directory Record (PDIR) @ RootDirectoryOffset -> Contains one file entry "file1.txt"
// 3. File Record (FILE) for "file1.txt" @ File1Offset -> Contains "Hello GGPK"
// 4. (Optional) Another File Record (FILE) for "file2_lz4.dat" @ File2Offset -> Contains LZ4 compressed "Compressed Data"

func buildTestGGPK(t *testing.T, withLZ4 bool) []byte {
	t.Helper()
	var ggpkData bytes.Buffer

	// --- Data Payloads ---
	file1Data := []byte("Hello GGPK")
	file2Payload := []byte("Some Long Compressed Data String, Repeated For Effect. Some Long Compressed Data String, Repeated For Effect.")
	var file2DataLZ4 []byte
	var file2UncompressedSize uint32

	if withLZ4 {
		file2UncompressedSize = uint32(len(file2Payload))
		compressedBuf := make([]byte, lz4.CompressBlockBound(len(file2Payload)))
		n, err := lz4.CompressBlock(file2Payload, compressedBuf, nil)
		if err != nil {
			t.Fatalf("Failed to compress test data: %v", err)
		}
		file2DataLZ4 = make([]byte, 4+n) // 4 for uncompressed size prefix
		binary.LittleEndian.PutUint32(file2DataLZ4[0:4], file2UncompressedSize)
		copy(file2DataLZ4[4:], compressedBuf[:n])
	}


	// --- Offsets (will be filled as we go) ---
	var currentOffset int64 = 0
	// ggpkRecordOffset was here, removed as it was unused. GGPK record starts at 0.

	// Placeholder for GGPK Record (28 bytes)
	ggpkRecordLen := int32(28)
	currentOffset += int64(ggpkRecordLen)
	rootDirOffset := currentOffset

	// --- Root Directory Record Content ---
	var rootDirEntries []DirectoryEntry
	var rootDirBody bytes.Buffer

	// File1 Entry (placeholder offset)
	file1Name := "file1.txt"
	file1Entry := DirectoryEntry{ NameHash: 0x1111, Offset: 0 /* placeholder */ } // Dummy hash
	rootDirEntries = append(rootDirEntries, file1Entry)

	// File2 Entry (if withLZ4)
	file2Name := "file2_lz4.dat"
	var file2Entry DirectoryEntry
	if withLZ4 {
		file2Entry = DirectoryEntry{ NameHash: 0x2222, Offset: 0 /* placeholder */}
		rootDirEntries = append(rootDirEntries, file2Entry)
	}

	// NameLength (root dir name is empty, but NameLength field might be 1 for null in some impls, or 0)
	// The C# code implies root dir name is "" and NameLength is from stream.
	// For our test, we'll assume an empty name string results in NameLength 1 (for null char)
	rootDirNameLenChars := uint32(1) // For null terminator of an empty string
	binary.Write(&rootDirBody, GGPKEndian, rootDirNameLenChars)
	binary.Write(&rootDirBody, GGPKEndian, uint32(len(rootDirEntries))) // EntryCount
	var rootDirHash [HashSize]byte; rootDirHash[0] = 0xAA;
	rootDirBody.Write(rootDirHash[:]) // Dummy Hash
	binary.Write(&rootDirBody, GGPKEndian, uint16(0)) // Null terminator for empty name (UTF-16 default)

	for _, entry := range rootDirEntries { // Write entry placeholders, offsets will be updated later
		binary.Write(&rootDirBody, GGPKEndian, entry.NameHash)
		binary.Write(&rootDirBody, GGPKEndian, entry.Offset) // Placeholder
	}
	rootDirLen := RecordHeaderSize + int32(rootDirBody.Len())
	currentOffset += int64(rootDirLen)
	file1Offset := currentOffset

	// --- File1 Record Content ---
	file1RecordBytes := createFileRecordBytes(t, file1Name, 3, file1Data)
	currentOffset += int64(len(file1RecordBytes))
	file2Offset := currentOffset

	// --- File2 Record Content (if withLZ4) ---
	var file2RecordBytes []byte
	if withLZ4 {
		file2RecordBytes = createFileRecordBytes(t, file2Name, 3, file2DataLZ4)
		currentOffset += int64(len(file2RecordBytes))
	}

	// --- Now fill in actual offsets ---
	rootDirEntries[0].Offset = file1Offset
	if withLZ4 {
		rootDirEntries[1].Offset = file2Offset
	}
	// Re-serialize rootDirBody with correct offsets
	rootDirBody.Reset()
	binary.Write(&rootDirBody, GGPKEndian, rootDirNameLenChars)
	binary.Write(&rootDirBody, GGPKEndian, uint32(len(rootDirEntries)))
	rootDirBody.Write(rootDirHash[:])
	binary.Write(&rootDirBody, GGPKEndian, uint16(0))
	for _, entry := range rootDirEntries {
		binary.Write(&rootDirBody, GGPKEndian, entry.NameHash)
		binary.Write(&rootDirBody, GGPKEndian, entry.Offset)
	}


	// --- Assemble the GGPK ---
	// 1. GGPK Record
	binary.Write(&ggpkData, GGPKEndian, ggpkRecordLen)
	binary.Write(&ggpkData, GGPKEndian, uint32(GGPKRecordTag))
	binary.Write(&ggpkData, GGPKEndian, uint32(3)) // Version
	binary.Write(&ggpkData, GGPKEndian, rootDirOffset)
	binary.Write(&ggpkData, GGPKEndian, int64(0)) // No free records for this simple test

	// 2. Root Directory Record
	binary.Write(&ggpkData, GGPKEndian, rootDirLen)
	binary.Write(&ggpkData, GGPKEndian, uint32(PDirRecordTag))
	ggpkData.Write(rootDirBody.Bytes())

	// 3. File1 Record
	ggpkData.Write(file1RecordBytes)

	// 4. File2 Record (if withLZ4)
	if withLZ4 {
		ggpkData.Write(file2RecordBytes)
	}

	// Sanity check final offset
	if ggpkData.Len() != int(currentOffset) {
		t.Fatalf("Calculated GGPK size %d does not match written GGPK size %d", currentOffset, ggpkData.Len())
	}

	return ggpkData.Bytes()
}

func TestOpenAndTraverse(t *testing.T) {
	testGGPKBytes := buildTestGGPK(t, true)
	filePath, cleanup := createTempFile(t, testGGPKBytes)
	defer cleanup()

	gf, err := Open(filePath)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer gf.Close()

	if gf.Header.Tag != GGPKRecordTag {
		t.Errorf("Expected GGPK tag in header, got %X", gf.Header.Tag)
	}
	if gf.Root == nil {
		t.Fatal("Root directory is nil after Open")
	}
	if gf.Root.Name != "" { // Root directory is unnamed in record, assigned "" by Open
		t.Errorf("Expected root directory name to be empty, got '%s'", gf.Root.Name)
	}

	// Traverse to file1.txt
	node, err := gf.GetNodeByPath("file1.txt")
	if err != nil {
		t.Fatalf("GetNodeByPath for 'file1.txt' failed: %v", err)
	}
	fileNode, ok := node.(*FileRecord)
	if !ok {
		t.Fatalf("Expected 'file1.txt' to be a FileRecord, got %T", node)
	}
	if fileNode.Name != "file1.txt" {
		t.Errorf("Expected file name 'file1.txt', got '%s'", fileNode.Name)
	}

	// Read file1.txt data
	data, err := gf.ReadFileData(fileNode)
	if err != nil {
		t.Fatalf("ReadFileData for 'file1.txt' failed: %v", err)
	}
	expectedData := "Hello GGPK"
	if string(data) != expectedData {
		t.Errorf("Expected data '%s', got '%s'", expectedData, string(data))
	}

	// Traverse to file2_lz4.dat
	nodeLz4, err := gf.GetNodeByPath("file2_lz4.dat")
	if err != nil {
		t.Fatalf("GetNodeByPath for 'file2_lz4.dat' failed: %v", err)
	}
	fileNodeLz4, ok := nodeLz4.(*FileRecord)
	if !ok {
		t.Fatalf("Expected 'file2_lz4.dat' to be a FileRecord, got %T", nodeLz4)
	}

	// Read file2_lz4.dat data (should be decompressed)
	dataLz4, err := gf.ReadFileData(fileNodeLz4)
	if err != nil {
		t.Fatalf("ReadFileData for 'file2_lz4.dat' failed: %v", err)
	}
	expectedDecompressed := "Some Long Compressed Data String, Repeated For Effect. Some Long Compressed Data String, Repeated For Effect."
	if string(dataLz4) != expectedDecompressed {
		t.Errorf("Expected decompressed data '%s', got '%s'", expectedDecompressed, string(dataLz4))
	}

	// Test non-existent file
	_, err = gf.GetNodeByPath("nonexistent.dat")
	if err == nil {
		t.Error("Expected error for non-existent file, got nil")
	}
}


// TestParseDirectoryRecordBody (basic, more complex scenarios covered by OpenAndTraverse)
func TestParseDirectoryRecordBody(t *testing.T) {
	// Root dir with 1 entry
	// NameLength = 1 (for null term of empty string)
	// EntryCount = 1
	// Hash = dummy
	// Name = "" (null term)
	// Entry1: NameHash=0xABCD, Offset=0x3000
	var dirBody bytes.Buffer
	binary.Write(&dirBody, GGPKEndian, uint32(1)) // NameLength (for null)
	binary.Write(&dirBody, GGPKEndian, uint32(1)) // EntryCount
	var hash [HashSize]byte; hash[0] = 0xDD;
	dirBody.Write(hash[:])
	binary.Write(&dirBody, GGPKEndian, uint16(0)) // Empty name (UTF-16 null)
	binary.Write(&dirBody, GGPKEndian, uint32(0xABCD)) // Entry1.NameHash
	binary.Write(&dirBody, GGPKEndian, int64(0x3000))   // Entry1.Offset

	var fullRecord bytes.Buffer
	recordLength := RecordHeaderSize + int32(dirBody.Len())
	binary.Write(&fullRecord, GGPKEndian, recordLength)
	binary.Write(&fullRecord, GGPKEndian, uint32(PDirRecordTag))
	fullRecord.Write(dirBody.Bytes())

	filePath, cleanup := createTempFile(t, fullRecord.Bytes())
	defer cleanup()
	f, err := os.Open(filePath); if err != nil { t.Fatal(err) }
	defer f.Close()

	gf := &GGPKFile{
		File: f,
		Header: GGPKRecord{Version: 3}, // PC version for UTF-16 names
		utf16LEDecoder: encunicode.UTF16(encunicode.LittleEndian, encunicode.IgnoreBOM).NewDecoder(),
		utf32LEDecoder: utf32encoding.UTF32(utf32encoding.LittleEndian, utf32encoding.IgnoreBOM).NewDecoder(),
		stringReadBuf:  make([]byte, 256),
	}

	lenFromFile, tagFromFile, err := gf.readRecordHeaderAndSeek(0)
	if err != nil { t.Fatalf("readRecordHeaderAndSeek failed: %v", err) }

	baseRec := BaseRecord{Offset: 0, Length: lenFromFile, Tag: tagFromFile}
	// For root dir, its name is "" and not parsed from the stream, but assigned.
	// Here we test parsing a generic dir, so assignedNameIfRoot is ""
	parsedDir, err := gf.parseDirectoryRecordBody(0, baseRec, "")
	if err != nil { t.Fatalf("parseDirectoryRecordBody failed: %v", err) }

	if parsedDir.Name != "" { // Expecting empty string from UTF-16 null
		t.Errorf("Expected dir name '', got '%s'", parsedDir.Name)
	}
	if parsedDir.EntryCount != 1 {
		t.Errorf("Expected entry count 1, got %d", parsedDir.EntryCount)
	}
	if len(parsedDir.Entries) != 1 {
		t.Fatalf("Expected 1 parsed entry, got %d", len(parsedDir.Entries))
	}
	if parsedDir.Entries[0].NameHash != 0xABCD {
		t.Errorf("Expected entry 0 NameHash 0xABCD, got %X", parsedDir.Entries[0].NameHash)
	}
	if parsedDir.Entries[0].Offset != 0x3000 {
		t.Errorf("Expected entry 0 Offset 0x3000, got %X", parsedDir.Entries[0].Offset)
	}
}

func TestReadFileData_EmptyFile(t *testing.T) {
	gf := &GGPKFile{} // Mock GGPKFile, doesn't need actual file for this test case
	fileRec := &FileRecord{DataLength: 0}
	data, err := gf.ReadFileData(fileRec)
	if err != nil {
		t.Fatalf("ReadFileData for empty file failed: %v", err)
	}
	if len(data) != 0 {
		t.Errorf("Expected 0 bytes for empty file, got %d", len(data))
	}
}

func TestReadFileData_NegativeLength(t *testing.T) {
	gf := &GGPKFile{}
	fileRec := &FileRecord{Name: "neg.txt", DataLength: -100}
	_, err := gf.ReadFileData(fileRec)
	if err == nil {
		t.Error("Expected error for negative DataLength, got nil")
	}
}

// Test specific case for ReadFileData where data is "uncompressed" but has LZ4 prefix
func TestReadFileData_UncompressedWithLZ4Prefix(t *testing.T) {
	payload := []byte("this data is not actually compressed")
	uncompressedSize := uint32(len(payload))

	// Create rawData with prefix + payload
	rawData := make([]byte, 4+len(payload))
	binary.LittleEndian.PutUint32(rawData[0:4], uncompressedSize)
	copy(rawData[4:], payload)

	// Create temp file with this rawData
	filePath, cleanup := createTempFile(t, rawData)
	defer cleanup()
	f, err := os.Open(filePath); if err != nil {t.Fatal(err)}
	defer f.Close()

	gf := &GGPKFile{File: f}
	fileRec := &FileRecord{
		Name: "prefixed_uncompressed.txt",
		DataOffset: 0, // Data starts at beginning of our temp file
		DataLength: int32(len(rawData)),
	}

	data, err := gf.ReadFileData(fileRec)
	if err != nil {
		t.Fatalf("ReadFileData failed: %v", err)
	}
	if string(data) != string(payload) {
		t.Errorf("Expected data '%s', got '%s'", string(payload), string(data))
	}
}

// Test ReadFileData where actual LZ4 decompression happens (covered by TestOpenAndTraverse)
// Test ReadFileData with data too short to be LZ4 (e.g. 2 bytes)
func TestReadFileData_TooShortForLZ4(t *testing.T) {
	payload := []byte{0x01, 0x02} // 2 bytes, too short for LZ4 prefix

	filePath, cleanup := createTempFile(t, payload)
	defer cleanup()
	f, err := os.Open(filePath); if err != nil {t.Fatal(err)}
	defer f.Close()

	gf := &GGPKFile{File: f}
	fileRec := &FileRecord{
		Name: "short.dat",
		DataOffset: 0,
		DataLength: int32(len(payload)),
	}
	data, err := gf.ReadFileData(fileRec)
	if err != nil {
		t.Fatalf("ReadFileData for short file failed: %v", err)
	}
	if !bytes.Equal(data, payload) {
		t.Errorf("Expected data %v, got %v", payload, data)
	}
}

// Test GetPath
func TestGetPath(t *testing.T) {
	root := &DirectoryRecord{BaseTreeNode: BaseTreeNode{parent: nil}, Name: ""}
	meta := &DirectoryRecord{BaseTreeNode: BaseTreeNode{parent: root}, Name: "Metadata"}
	items := &DirectoryRecord{BaseTreeNode: BaseTreeNode{parent: meta}, Name: "Items"}
	fileDat := &FileRecord{BaseTreeNode: BaseTreeNode{parent: items}, Name: "file.dat"}

	if root.GetPath() != "" {
		t.Errorf("Expected root path '', got '%s'", root.GetPath())
	}
	if meta.GetPath() != "Metadata" {
		t.Errorf("Expected meta path 'Metadata', got '%s'", meta.GetPath())
	}
	if items.GetPath() != "Metadata/Items" {
		t.Errorf("Expected items path 'Metadata/Items', got '%s'", items.GetPath())
	}
	if fileDat.GetPath() != "Metadata/Items/file.dat" {
		t.Errorf("Expected file.dat path 'Metadata/Items/file.dat', got '%s'", fileDat.GetPath())
	}

	// Test file directly under root
	rootFile := &FileRecord{BaseTreeNode: BaseTreeNode{parent: root}, Name: "rootfile.txt"}
	if rootFile.GetPath() != "rootfile.txt" {
		t.Errorf("Expected rootfile.txt path 'rootfile.txt', got '%s'", rootFile.GetPath())
	}
}

// TODO: Add tests for FreeRecord parsing if it becomes more complex than just NextFreeOffset.
// TODO: Add tests for Murmur2Hash generation if/when implemented for name hash lookups.

func TestMain(m *testing.M) {
	// Can set up global test resources here if needed
	exitCode := m.Run()
	// Can tear down global test resources here if needed
	os.Exit(exitCode)
}
