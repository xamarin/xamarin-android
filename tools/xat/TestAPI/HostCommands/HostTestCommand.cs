using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Host
{
	abstract class HostTestCommand : TestCommand
	{
		public AndroidState? State                 { get; set; }

		public HostTestCommand (string name, string description)
			: base (name, description)
		{}

		protected override async Task<bool> Execute (XATest test)
		{
			if (!(test is TestHostUnit hostTest)) {
				throw new InvalidOperationException ("Test must be of the TestHostUnit type");
			}

			return await Run (hostTest);
		}

		protected override void SetState (TestCommand command)
		{
			if (command is HostTestCommand hostCommand) {
				hostCommand.State = State;
				return;
			}

			throw new InvalidOperationException ($"Invalid command type {command}");
		}

		protected abstract Task<bool> Run (TestHostUnit test);
	}
}
