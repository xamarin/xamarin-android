using System.Collections.Generic;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

abstract class InputTypeDetector
{
	public string? FailureReason { get; protected set; }
	public List<InputTypeDetector> Nested { get; } = new List<InputTypeDetector> ();

	/// <summary>
	/// If file is acceptable for input, returns <c>accepted</c> set to <c>true</c>.  If the detection is final, that is an input reader is found, <c>reader</c> will be set to
	/// instance of apropriate reader. If <c>reader</c> is <c>null</c>, however, then detection should continue by querying the <see cref="Nested"/> detectors.
	/// </summary>
	public abstract (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log);
}
