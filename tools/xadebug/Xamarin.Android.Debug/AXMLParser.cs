using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

enum ChunkType : ushort
{
	Null              = 0x0000,
	StringPool        = 0x0001,
	Table             = 0x0002,
	Xml               = 0x0003,

	XmlFirstChunk     = 0x0100,
	XmlStartNamespace = 0x0100,
	XmlEndNamespace   = 0x0101,
	XmlStartElement   = 0x0102,
	XmlEndElement     = 0x0103,
	XmlCData          = 0x0104,
	XmlLastChunk      = 0x017f,
	XmlResourceMap    = 0x0180,

	TablePackage      = 0x0200,
	TableType         = 0x0201,
	TableTypeSpec     = 0x0202,
	TableLibrary      = 0x0203,
}

enum AttributeType : uint
{
	// The 'data' field is either 0 or 1, specifying this resource is either undefined or empty, respectively.
	Null = 0x00,

	// The 'data' field holds a ResTable_ref, a reference to another resource
	Reference = 0x01,

	// The 'data' field holds an attribute resource identifier.
	Attribute = 0x02,

	// The 'data' field holds an index into the containing resource table's global value string pool.
	String = 0x03,

	// The 'data' field holds a single-precision floating point number.
	Float = 0x04,

	// The 'data' holds a complex number encoding a dimension value such as "100in".
	Dimension = 0x05,

	// The 'data' holds a complex number encoding a fraction of a container.
	Fraction = 0x06,

	// The 'data' holds a dynamic ResTable_ref, which needs to be resolved before it can be used like a Reference
	DynamicReference = 0x07,

	// The 'data' holds an attribute resource identifier, which needs to be resolved before it can be used like a Attribute.
	DynamicAttribute = 0x08,

	// The 'data' is a raw integer value of the form n..n.
	IntDec = 0x10,

	// The 'data' is a raw integer value of the form 0xn..n.
	IntHex = 0x11,

	// The 'data' is either 0 or 1, for input "false" or "true" respectively.
	IntBoolean = 0x12,

	// The 'data' is a raw integer value of the form #aarrggbb.
	IntColorARGB8 = 0x1c,

	// The 'data' is a raw integer value of the form #rrggbb.
	IntColorRGB8 = 0x1d,

	// The 'data' is a raw integer value of the form #argb.
	IntColorARGB4 = 0x1e,

	// The 'data' is a raw integer value of the form #rgb.
	IntColorRGB4 = 0x1f,
}

//
// Based on https://github.com/androguard/androguard/tree/832104db3eb5dc3cc66b30883fa8ce8712dfa200/androguard/core/axml
//
class AXMLParser
{
	// Position of fields inside an attribute
	const int ATTRIBUTE_IX_NAMESPACE_URI = 0;
	const int ATTRIBUTE_IX_NAME          = 1;
	const int ATTRIBUTE_IX_VALUE_STRING  = 2;
	const int ATTRIBUTE_IX_VALUE_TYPE    = 3;
	const int ATTRIBUTE_IX_VALUE_DATA    = 4;
	const int ATTRIBUTE_LENGHT           = 5;

	const long MinimumDataSize           = 8;
	const long MaximumDataSize           = (long)UInt32.MaxValue;

	const uint ComplexUnitMask           = 0x0f;

	static readonly float[] RadixMultipliers = {
		0.00390625f,
		3.051758E-005f,
		1.192093E-007f,
		4.656613E-010f,
	};

	static readonly string[] DimensionUnits = {
		"px",
		"dip",
		"sp",
		"pt",
		"in",
		"mm",
	};

	static readonly string[] FractionUnits = {
		"%",
		"%p",
	};

	readonly XamarinLoggingHelper log;

	Stream data;
	long dataSize;
	ARSCHeader axmlHeader;
	uint fileSize;
	StringBlock stringPool;
	bool valid = true;
	long initialPosition;

	public bool IsValid => valid;

	public AXMLParser (Stream data, XamarinLoggingHelper logger)
	{
		log = logger;

		this.data = data;
		dataSize = data.Length;

		// Minimum is a single ARSCHeader, which would be a strange edge case...
		if (dataSize < MinimumDataSize) {
			throw new InvalidDataException ($"Input data size too small for it to be valid AXML content ({dataSize} < {MinimumDataSize})");
		}

		// This would be even stranger, if an AXML file is larger than 4GB...
		// But this is not possible as the maximum chunk size is a unsigned 4 byte int.
		if (dataSize > MaximumDataSize) {
			throw new InvalidDataException ($"Input data size too large for it to be a valid AXML content ({dataSize} > {MaximumDataSize})");
		}

		try {
			axmlHeader = new ARSCHeader (data);
		} catch (Exception) {
			log.ErrorLine ("Error parsing the first data header");
			throw;
		}

		if (axmlHeader.HeaderSize != 8) {
			throw new InvalidDataException ($"This does not look like AXML data. header size does not equal 8. header size = {axmlHeader.Size}");
		}

		fileSize = axmlHeader.Size;
		if (fileSize > dataSize) {
			throw new InvalidDataException ($"This does not look like AXML data. Declared data size does not match real size: {fileSize} vs {dataSize}");
		}

		if (fileSize < dataSize) {
			log.WarningLine ($"Declared data size ({fileSize}) is smaller than total data size ({dataSize}). Was something appended to the file? Trying to parse it anyways.");
		}

		if (axmlHeader.Type != ChunkType.Xml) {
			log.WarningLine ($"AXML file has an unusual resource type, trying to parse it anyways. Resource Type: 0x{(ushort)axmlHeader.Type:04x}");
		}

		ARSCHeader stringPoolHeader = new ARSCHeader (data, ChunkType.StringPool);
		if (stringPoolHeader.HeaderSize != 28) {
			throw new InvalidDataException ($"This does not look like an AXML file. String chunk header size does not equal 28. Header size = {stringPoolHeader.Size}");
		}

		stringPool = new StringBlock (logger, data, stringPoolHeader);
		initialPosition = data.Position;
	}

	public XmlDocument? Parse ()
	{
		// Reset position in case we're called more than once, for whatever reason
		data.Seek (initialPosition, SeekOrigin.Begin);
		valid = true;

		XmlDocument ret = new XmlDocument ();
		XmlDeclaration declaration = ret.CreateXmlDeclaration ("1.0", stringPool.IsUTF8 ? "UTF-8" : "UTF-16", null);
		ret.InsertBefore (declaration, ret.DocumentElement);

		using var reader = new BinaryReader (data, Encoding.UTF8, leaveOpen: true);
		ARSCHeader? header;
		string? nsPrefix = null;
		string? nsUri = null;
		uint prefixIndex = 0;
		uint uriIndex = 0;
		var nsUriToPrefix = new Dictionary<string, string> (StringComparer.Ordinal);
		XmlNode? currentNode = ret.DocumentElement;

		while (data.Position < dataSize) {
			header = new ARSCHeader (data);

			// Special chunk: Resource Map. This chunk might follow the string pool.
			if (header.Type == ChunkType.XmlResourceMap) {
				if (!SkipOverResourceMap (header, reader)) {
					valid = false;
					break;
				}
				continue;
			}

			// XML chunks

			// Skip over unknown types
			if (!Enum.IsDefined (typeof(ChunkType), header.TypeRaw)) {
				log.WarningLine ($"Unknown chunk type 0x{header.TypeRaw:x} at offset {data.Position}. Skipping over {header.Size} bytes");
				data.Seek (header.Size, SeekOrigin.Current);
				continue;
			}

			// Check that we read a correct header
			if (header.HeaderSize != 16) {
				log.WarningLine ($"XML chunk header size is not 16. Chunk type {header.Type} (0x{header.TypeRaw:x}), chunk size {header.Size}");
				data.Seek (header.Size, SeekOrigin.Current);
				continue;
			}

			// Line Number of the source file, only used as meta information
			uint lineNumber = reader.ReadUInt32 ();

			// Comment_Index (usually 0xffffffff)
			uint commentIndex = reader.ReadUInt32 ();

			if (commentIndex != 0xffffffff && (header.Type == ChunkType.XmlStartNamespace || header.Type == ChunkType.XmlEndNamespace)) {
				log.WarningLine ($"Unhandled Comment at namespace chunk: {commentIndex}");
			}

			if (header.Type == ChunkType.XmlStartNamespace) {
				prefixIndex = reader.ReadUInt32 ();
				uriIndex = reader.ReadUInt32 ();

				nsPrefix = stringPool.GetString (prefixIndex);
				nsUri = stringPool.GetString (uriIndex);

				if (!String.IsNullOrEmpty (nsUri)) {
					nsUriToPrefix[nsUri] = nsPrefix ?? String.Empty;
				}

				log.DebugLine ($"Start of Namespace mapping: prefix {prefixIndex}: '{nsPrefix}' --> uri {uriIndex}: '{nsUri}'");

				if (String.IsNullOrEmpty (nsUri)) {
					log.WarningLine ($"Namespace prefix '{nsPrefix}' resolves to empty URI.");
				}

				continue;
			}

			if (header.Type == ChunkType.XmlEndNamespace) {
				// Namespace handling is **really** simplified, since we expect to deal only with AndroidManifest.xml which should have just one namespace.
				// There should be no problems with that. Famous last words.
				uint endPrefixIndex = reader.ReadUInt32 ();
				uint endUriIndex = reader.ReadUInt32 ();

				log.DebugLine ($"End of Namespace mapping: prefix {endPrefixIndex}, uri {endUriIndex}");
				if (endPrefixIndex != prefixIndex) {
					log.WarningLine ($"Prefix index of Namespace end doesn't match the last Namespace prefix index: {prefixIndex} != {endPrefixIndex}");
				}

				if (endUriIndex != uriIndex) {
					log.WarningLine ($"URI index of Namespace end doesn't match the last Namespace URI index: {uriIndex} != {endUriIndex}");
				}

				string? endUri = stringPool.GetString (endUriIndex);
				if (!String.IsNullOrEmpty (endUri) && nsUriToPrefix.ContainsKey (endUri)) {
					nsUriToPrefix.Remove (endUri);
				}

				nsPrefix = null;
				nsUri = null;
				prefixIndex = 0;
				uriIndex = 0;

				continue;
			}

			uint tagNsUriIndex;
			uint tagNameIndex;
			string? tagName;
//			string? tagNs; // TODO: implement

			if (header.Type == ChunkType.XmlStartElement) {
				// The TAG consists of some fields:
				// * (chunk_size, line_number, comment_index - we read before)
				// * namespace_uri
				// * name
				// * flags
				// * attribute_count
				// * class_attribute
				// After that, there are two lists of attributes, 20 bytes each
				tagNsUriIndex = reader.ReadUInt32 ();
				tagNameIndex = reader.ReadUInt32 ();
				uint tagFlags = reader.ReadUInt32 ();
				uint attributeCount = reader.ReadUInt32 () & 0xffff;
				uint classAttribute = reader.ReadUInt32 ();

				// Tag name is, of course, required but instead of throwing an exception should we find none, we use a fake name in hope that we can still salvage
				// the document.
				tagName = stringPool.GetString (tagNameIndex) ?? "unnamedTag";
				log.DebugLine ($"Start of tag '{tagName}', NS URI index {tagNsUriIndex}");
				log.DebugLine ($"Reading tag attributes ({attributeCount}):");

				string? tagNsUri = tagNsUriIndex != 0xffffffff ? stringPool.GetString (tagNsUriIndex) : null;
				string? tagNsPrefix;

				if (String.IsNullOrEmpty (tagNsUri) || !nsUriToPrefix.TryGetValue (tagNsUri, out tagNsPrefix)) {
					tagNsPrefix = null;
				}

				XmlElement element = ret.CreateElement (tagNsPrefix, tagName, tagNsUri);
				if (currentNode == null) {
					ret.AppendChild (element);
					if (!String.IsNullOrEmpty (nsPrefix) && !String.IsNullOrEmpty (nsUri)) {
						ret.DocumentElement!.SetAttribute ($"xmlns:{nsPrefix}", nsUri);
					}
				} else {
					currentNode.AppendChild (element);
				}
				currentNode = element;

				for (uint i = 0; i < attributeCount; i++) {
					uint attrNsIdx = reader.ReadUInt32 (); // string index
					uint attrNameIdx = reader.ReadUInt32 (); // string index
					uint attrValue = reader.ReadUInt32 ();
					uint attrType = reader.ReadUInt32 () >> 24;
					uint attrData = reader.ReadUInt32 ();

					string? attrNs = attrNsIdx != 0xffffffff ? stringPool.GetString (attrNsIdx) : String.Empty;
					string? attrName = stringPool.GetString (attrNameIdx);

					if (String.IsNullOrEmpty (attrName)) {
						log.WarningLine ($"Attribute without name, ignoring. Offset: {data.Position}");
						continue;
					}

					log.DebugLine ($"  '{attrName}': ns == '{attrNs}'; value == 0x{attrValue:x}; type == 0x{attrType:x}; data == 0x{attrData:x}");
					XmlAttribute attr;

					if (!String.IsNullOrEmpty (attrNs)) {
						attr = ret.CreateAttribute (nsUriToPrefix[attrNs], attrName, attrNs);
					} else {
						attr = ret.CreateAttribute (attrName!);
					}
					attr.Value = GetAttributeValue (attrValue, attrType, attrData);
					element.SetAttributeNode (attr);
				}
				continue;
			}

			if (header.Type == ChunkType.XmlEndElement) {
				tagNsUriIndex = reader.ReadUInt32 ();
				tagNameIndex = reader.ReadUInt32 ();

				tagName = stringPool.GetString (tagNameIndex);
				log.DebugLine ($"End of tag '{tagName}', NS URI index {tagNsUriIndex}");
				currentNode = currentNode?.ParentNode!;
				continue;
			}

			// TODO: add support for CDATA
		}

		return ret;
	}

	string GetAttributeValue (uint attrValue, uint attrType, uint attrData)
	{
		if (!Enum.IsDefined (typeof(AttributeType), attrType)) {
			log.WarningLine ($"Unknown attribute type value 0x{attrType:x}, returning empty attribute value (data == 0x{attrData:x}). Offset: {data.Position}");
			return String.Empty;
		}

		switch ((AttributeType)attrType) {
			case AttributeType.Null:
				return attrData == 0 ? "?NULL?" : String.Empty;

			case AttributeType.Reference:
				return $"@{MaybePrefix()}{attrData:x08}";

			case AttributeType.Attribute:
				return $"?{MaybePrefix()}{attrData:x08}";

			case AttributeType.String:
				return stringPool.GetString (attrData) ?? String.Empty;

			case AttributeType.Float:
				return $"{(float)attrData}";

			case AttributeType.Dimension:
				return $"{ComplexToFloat(attrData)}{DimensionUnits[attrData & ComplexUnitMask]}";

			case AttributeType.Fraction:
				return $"{ComplexToFloat(attrData) * 100.0f}{FractionUnits[attrData & ComplexUnitMask]}";

			case AttributeType.IntDec:
				return attrData.ToString ();

			case AttributeType.IntHex:
				return $"0x{attrData:X08}";

			case AttributeType.IntBoolean:
				return attrData == 0 ? "false" : "true";

			case AttributeType.IntColorARGB8:
			case AttributeType.IntColorRGB8:
			case AttributeType.IntColorARGB4:
			case AttributeType.IntColorRGB4:
				return $"#{attrData:X08}";
		}

		return String.Empty;

		string MaybePrefix ()
		{
			if (attrData >> 24 == 1) {
				return "android:";
			}
			return String.Empty;
		}

		float ComplexToFloat (uint value)
		{
			return (float)(value & 0xffffff00) * RadixMultipliers[(value >> 4) & 3];
		}
	}

	bool SkipOverResourceMap (ARSCHeader header, BinaryReader reader)
	{
		log.DebugLine ("AXML contains a resource map");

		// Check size: < 8 bytes mean that the chunk is not complete
		// Should be aligned to 4 bytes.
		if (header.Size < 8 || (header.Size % 4) != 0) {
			log.ErrorLine ("Invalid chunk size in chunk XML_RESOURCE_MAP");
			return false;
		}

		// Since our main interest is in reading AndroidManifest.xml, we're going to skip over the table
		for (int i = 0; i < (header.Size - header.HeaderSize) / 4; i++) {
			reader.ReadUInt32 ();
		}

		return true;
	}
}

class StringBlock
{
	const uint FlagSorted = 1 << 0;
	const uint FlagUTF8   = 1 << 0;

	XamarinLoggingHelper log;
	ARSCHeader header;
	uint stringCount;
	uint stringsOffset;
	uint flags;
	bool isUTF8;
	List<uint> stringOffsets;
	byte[] chars;
	Dictionary<uint, string> stringCache;

	public uint StringCount => stringCount;
	public bool IsUTF8 => isUTF8;

	public StringBlock (XamarinLoggingHelper logger, Stream data, ARSCHeader stringPoolHeader)
	{
		log = logger;
		header = stringPoolHeader;

		using var reader = new BinaryReader (data, Encoding.UTF8, leaveOpen: true);

		stringCount = reader.ReadUInt32 ();
		uint styleCount = reader.ReadUInt32 ();

		flags = reader.ReadUInt32 ();
		isUTF8 = (flags & FlagUTF8) == FlagUTF8;

		stringsOffset = reader.ReadUInt32 ();
		uint stylesOffset = reader.ReadUInt32 ();

		if (styleCount == 0 && stylesOffset > 0) {
			log.InfoLine ("Styles Offset given, but styleCount is zero. This is not a problem but could indicate packers.");
		}

		stringOffsets = new List<uint> ();

		for (uint i = 0; i < stringCount; i++) {
			stringOffsets.Add (reader.ReadUInt32 ());
		}

		// We're not interested in styles, skip over their offsets
		for (uint i = 0; i < styleCount; i++) {
			reader.ReadUInt32 ();
		}

		bool haveStyles = stylesOffset != 0 && styleCount != 0;
		uint size = header.Size - stringsOffset;
		if (haveStyles) {
			size = stylesOffset - stringsOffset;
		}

		if (size % 4 != 0) {
			log.WarningLine ("Size of strings is not aligned on four bytes.");
		}

		chars = new byte[size];
		reader.Read (chars, 0, (int)size);

		if (haveStyles) {
			size = header.Size - stylesOffset;

			if (size % 4 != 0) {
				log.WarningLine ("Size of styles is not aligned on four bytes.");
			}

			// Not interested in them, skip
			for (uint i = 0; i < size / 4; i++) {
				reader.ReadUInt32 ();
			}
		}

		stringCache = new Dictionary<uint, string> ();
	}

	public string? GetString (uint idx)
	{
		if (stringCache.TryGetValue (idx, out string? ret)) {
			return ret;
		}

		if (idx < 0 || idx > stringOffsets.Count || stringOffsets.Count == 0) {
			return null;
		}

		uint offset = stringOffsets[(int)idx];
		if (isUTF8) {
			ret = DecodeUTF8 (offset);
		} else {
			ret = DecodeUTF16 (offset);
		}
		stringCache[idx] = ret;

		return ret;
	}

	string DecodeUTF8 (uint offset)
	{
		// UTF-8 Strings contain two lengths, as they might differ:
		// 1) the string length in characters
		(uint length, uint nbytes) = DecodeLength (offset, sizeOfChar: 1);
		offset += nbytes;

		// 2) the number of bytes the encoded string occupies
		(uint encodedBytes, nbytes) = DecodeLength (offset, sizeOfChar: 1);
		offset += nbytes;

		if (chars[offset + encodedBytes] != 0) {
			throw new InvalidDataException ($"UTF-8 string is not NUL-terminated. Offset: offset");
		}

		return Encoding.UTF8.GetString (chars, (int)offset, (int)encodedBytes);
	}

	string DecodeUTF16 (uint offset)
	{
		(uint length, uint nbytes) = DecodeLength (offset, sizeOfChar: 2);
		offset += nbytes;

		uint encodedBytes = length * 2;
		if (chars[offset + encodedBytes] != 0 && chars[offset + encodedBytes + 1] != 0) {
			throw new InvalidDataException ($"UTF-16 string is not NUL-terminated. Offset: offset");
		}

		return Encoding.Unicode.GetString (chars, (int)offset, (int)encodedBytes);
	}

	(uint length, uint nbytes) DecodeLength (uint offset, uint sizeOfChar)
	{
		uint sizeOfTwoChars = sizeOfChar << 1;
		uint highBit = 0x80u << (8 * ((int)sizeOfChar - 1));
		uint length1, length2;

		// Length is tored as 1 or 2 characters of `sizeofChar` size
		if (sizeOfChar == 1) {
			// UTF-8 encoding, each character is a byte
			length1 = chars[offset];
			length2 = chars[offset + 1];
		} else {
			// UTF-16 encoding, each character is a short
			length1 = (uint)((chars[offset]) | (chars[offset + 1] << 8));
			length2 = (uint)((chars[offset + 2]) | (chars[offset + 3] << 8));
		}

		uint length;
		uint nbytes;
		if ((length1 & highBit) != 0) {
			length = ((length1 & ~highBit) << (8 * (int)sizeOfChar)) | length2;
			nbytes = sizeOfTwoChars;
		} else {
			length = length1;
			nbytes = sizeOfChar;
		}

		// 8 bit strings: maximum of 0x7FFF bytes, http://androidxref.com/9.0.0_r3/xref/frameworks/base/libs/androidfw/ResourceTypes.cpp#692
		// 16 bit strings: maximum of 0x7FFFFFF bytes, http://androidxref.com/9.0.0_r3/xref/frameworks/base/libs/androidfw/ResourceTypes.cpp#670
		if (sizeOfChar == 1) {
			if (length > 0x7fff) {
				throw new InvalidDataException ("UTF-8 string is too long. Offset: {offset}");
			}
		} else {
			if (length > 0x7fffffff) {
				throw new InvalidDataException ("UTF-16 string is too long. Offset: {offset}");
			}
		}

		return (length, nbytes);
	}
}

class ARSCHeader
{
	// This is the minimal size such a header must have. There might be other header data too!
	const long MinimumSize = 2 + 2 + 4;

	long start;
	uint size;
	ushort type;
	ushort headerSize;
	bool unknownType;

	public ChunkType Type    => unknownType ? ChunkType.Null : (ChunkType)type;
	public ushort TypeRaw    => type;
	public ushort HeaderSize => headerSize;
	public uint Size         => size;
	public long End          => start + (long)size;

	public ARSCHeader (Stream data, ChunkType? expectedType = null)
	{
		start = data.Position;
		if (data.Length < start + MinimumSize) {
			throw new InvalidDataException ($"Input data not large enough. Offset: {start}");
		}

		// Data in AXML is little-endian, which is fortuitous as that's the only format BinaryReader understands.
		using BinaryReader reader = new BinaryReader (data, Encoding.UTF8, leaveOpen: true);

		// ushort: type
		// ushort: header_size
		// uint: size
		type = reader.ReadUInt16 ();
		headerSize = reader.ReadUInt16 ();

		// Total size of the chunk, including the header
		size = reader.ReadUInt32 ();

		if (expectedType != null && type != (ushort)expectedType) {
			throw new InvalidOperationException ($"Header type is not equal to the expected type ({expectedType}): got 0x{type:x}, expected 0x{(ushort)expectedType:x}");
		}

		unknownType = !Enum.IsDefined (typeof(ChunkType), type);

		if (headerSize < MinimumSize) {
			throw new InvalidDataException ($"Declared header size is smaller than required size of {MinimumSize}. Offset: {start}");
		}

		if (size < MinimumSize) {
			throw new InvalidDataException ($"Declared chunk size is smaller than required size of {MinimumSize}. Offset: {start}");
		}

		if (size < headerSize) {
			throw new InvalidDataException ($"Declared chunk size ({size}) is smaller than header size ({headerSize})! Offset: {start}");
		}
	}
}
