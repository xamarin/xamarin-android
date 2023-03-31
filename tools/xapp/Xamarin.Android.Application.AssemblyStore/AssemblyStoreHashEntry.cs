using System.IO;

namespace Xamarin.Android.AssemblyStore
{
	class AssemblyStoreHashEntry
	{
		public bool Is32Bit         { get; }

		public ulong Hash           { get; }
		public uint MappingIndex    { get; }
		public uint LocalStoreIndex { get; }
		public uint StoreID         { get; }

		internal AssemblyStoreHashEntry (BinaryReader reader, bool is32Bit)
		{
			Is32Bit = is32Bit;

			Hash = reader.ReadUInt64 ();
			MappingIndex = reader.ReadUInt32 ();
			LocalStoreIndex = reader.ReadUInt32 ();
			StoreID = reader.ReadUInt32 ();
		}
	}
}
