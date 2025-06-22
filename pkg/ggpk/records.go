package ggpk

import "encoding/binary"

// Endianness used in GGPK files
var GGPKEndian = binary.LittleEndian

// RecordHeaderSize is the common size of the record length and tag
const RecordHeaderSize = 8 // int32 for length + int32 for tag

// HashSize is the size of SHA256 hashes used in GGPK records
const HashSize = 32

// Tags for different record types
const (
	GGPKRecordTag = 0x4B504747 // "GGPK"
	FreeRecordTag = 0x45455246 // "FREE"
	FileRecordTag = 0x454C4946 // "FILE"
	PDirRecordTag = 0x52494450 // "PDIR"
)

// BaseRecord contains common fields for all records.
// This is not a direct representation of a stored struct but a conceptual base.
type BaseRecord struct {
	Offset int64 // Offset in the pack file where the record begins
	Length int32 // Length of the entire record in bytes
	Tag    uint32
}

// GGPKRecord is the main header record of the GGPK file.
type GGPKRecord struct {
	BaseRecord
	Version             uint32 // 3 for PC, 4 for Mac, 2 for older versions
	RootDirectoryOffset int64
	FirstFreeOffset     int64
}

// FreeRecord represents a block of free space in the GGPK file.
type FreeRecord struct {
	BaseRecord
	NextFreeOffset int64
	// The rest of the record's Length is unused space
}

// DirectoryEntry represents an entry (file or subdirectory) within a DirectoryRecord.
type DirectoryEntry struct {
	NameHash uint32 // Murmur2 hash of the lowercase entry name
	Offset   int64  // Offset in pack file where the record for this entry begins
}

// TreeNode is an interface that FileRecord and DirectoryRecord will implement
// for easier traversal of the GGPK structure.
type TreeNode interface {
	GetName() string
	GetPath() string // Full path from root
	GetParent() *DirectoryRecord
	SetParent(parent *DirectoryRecord)
}

// BaseTreeNode provides a common implementation for Parent.
type BaseTreeNode struct {
	parent *DirectoryRecord
}

// GetParent returns the parent directory of this node.
func (btn *BaseTreeNode) GetParent() *DirectoryRecord {
	return btn.parent
}

// SetParent sets the parent directory of this node.
func (btn *BaseTreeNode) SetParent(parent *DirectoryRecord) {
	btn.parent = parent
}

// FileRecord represents a file stored within the GGPK archive.
type FileRecord struct {
	BaseRecord
	BaseTreeNode // Embed for Parent management
	NameLength   uint32 // Length of the name in characters (includes null terminator)
	Hash         [HashSize]byte
	Name         string // Decoded name
	DataOffset   int64  // Offset where the actual file data begins
	DataLength   int32  // Length of the file data
}

// DirectoryRecord represents a directory within the GGPK archive.
type DirectoryRecord struct {
	BaseRecord
	BaseTreeNode      // Embed for Parent management
	NameLength        uint32 // Length of the name in characters (includes null terminator)
	EntryCount        uint32
	Hash              [HashSize]byte // SHA256 hash of the combined hashes of all entries
	Name              string         // Decoded name
	Entries           []DirectoryEntry
	Children          []TreeNode // Can be *FileRecord or *DirectoryRecord, resolved during parsing. This was changed from interface{}
	childRecordsDirty bool       // Flag to indicate if Children need reparsing
}


// Ensure FileRecord implements TreeNode (compile-time check)
var _ TreeNode = (*FileRecord)(nil)

// GetName returns the name of the file.
func (fr *FileRecord) GetName() string {
	return fr.Name
}

// GetPath returns the full path of the file.
func (fr *FileRecord) GetPath() string {
	if fr.parent == nil || (fr.parent.Name == "" && fr.parent.parent == nil) { // Handle root directory parent or unparented node
		return fr.Name
	}
	return fr.parent.GetPath() + "/" + fr.Name
}

// Ensure DirectoryRecord implements TreeNode (compile-time check)
var _ TreeNode = (*DirectoryRecord)(nil)

// GetName returns the name of the directory.
func (dr *DirectoryRecord) GetName() string {
	return dr.Name
}

// GetPath returns the full path of the directory.
func (dr *DirectoryRecord) GetPath() string {
	if dr.Name == "" && dr.parent == nil { // Root directory special case
		return ""
	}
	if dr.parent == nil {
		return dr.Name
	}

	parentPath := dr.parent.GetPath()
	if parentPath == "" { // If parent is root
		return dr.Name
	}
	return parentPath + "/" + dr.Name
}
