using System;

namespace Xamarin.Android.Prepare
{
	[Scenario (isDefault: false)]
	partial class Scenario_ProvisionMSOpenJDK : ScenarioNoStandardEndSteps
	{
		public Scenario_ProvisionMSOpenJDK ()
			: base ("ProvisionMSOpenJDK", "Provision MS OpenJDK.", Context.Instance)
		{}

		protected override void AddSteps (Context context)
		{
			Steps.Add (new Step_ProvisionMSOpenJDK ());

			// disable installation of missing programs...
			context.SetCondition (KnownConditions.AllowProgramInstallation, false);

			// ...but do not signal an error when any are missing
			context.SetCondition (KnownConditions.IgnoreMissingPrograms, true);
		}
	}
}
