using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.APK
{
	abstract class APKTestCommand : TestCommand
	{
		public APKState? State { get; set; }

		public APKTestCommand (string name, string description)
			: base (name, description)
		{}

		protected override async Task<bool> Execute (XATest test)
		{
			if (State == null) {
				throw new InvalidOperationException ("State not set");
			}

			if (!(test is TestAPK apkTest)) {
				throw new InvalidOperationException ("Test must be of the TestAPK type");
			}

			return await Run (apkTest);
		}

		protected override void SetState (TestCommand command)
		{
			if (command is APK.APKTestCommand apkCommand) {
				apkCommand.State = State;
				return;
			}

			throw new InvalidOperationException ($"Invalid command type {command}");
		}

		protected abstract Task<bool> Run (TestAPK test);
	}
}
