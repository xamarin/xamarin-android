namespace Xamarin.Android.Prepare
{
	class TestHostUnit : XATest
	{
		public override string KindName => "Host Unit";
		public string TestAssemblyPath => TestFilePath;

		public TestHostUnit (string name, string testFilePath)
			: base (name, testFilePath)
		{}
	}
}
