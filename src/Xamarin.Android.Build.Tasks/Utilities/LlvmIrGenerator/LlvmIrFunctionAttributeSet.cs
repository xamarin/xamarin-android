using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks.LLVMIR;

class LlvmIrFunctionAttributeSet : IEnumerable<LlvmIrFunctionAttribute>, IEquatable<LlvmIrFunctionAttributeSet>
{
	public uint Number                           { get; set; } = 0;
	public bool DoNotAddTargetSpecificAttributes { get; set; }

	HashSet<LlvmIrFunctionAttribute> attributes;
	Dictionary<AndroidTargetArch, List<LlvmIrFunctionAttribute>>? privateTargetSpecificAttributes;

	public LlvmIrFunctionAttributeSet ()
	{
		attributes = new HashSet<LlvmIrFunctionAttribute> ();
	}

	public LlvmIrFunctionAttributeSet (LlvmIrFunctionAttributeSet other)
	{
		attributes = new HashSet<LlvmIrFunctionAttribute> (other);
		Number = other.Number;
	}

	public IList<LlvmIrFunctionAttribute>? GetPrivateTargetAttributes (AndroidTargetArch targetArch)
	{
		if (privateTargetSpecificAttributes == null || !privateTargetSpecificAttributes.TryGetValue (targetArch, out List<LlvmIrFunctionAttribute> list)) {
			return null;
		}

		return list.AsReadOnly ();
	}

	public void Add (LlvmIrFunctionAttribute attr)
	{
		if (attr == null) {
			throw new ArgumentNullException (nameof (attr));
		}

		if (!attributes.Contains (attr)) {
			attributes.Add (attr);
		}
	}

	public void Add (IList<LlvmIrFunctionAttribute> attrList)
	{
		foreach (LlvmIrFunctionAttribute attr in attrList) {
			Add (attr);
		}
	}

	public void Add (AndroidTargetArch targetArch, LlvmIrFunctionAttribute attr)
	{
		if (privateTargetSpecificAttributes == null) {
			privateTargetSpecificAttributes = new ();
		}

		if (!privateTargetSpecificAttributes.TryGetValue (targetArch, out List<LlvmIrFunctionAttribute> attrList)) {
			attrList = new ();
			privateTargetSpecificAttributes.Add (targetArch, attrList);
		}
		attrList.Add (attr);
	}

	public string Render ()
	{
		List<LlvmIrFunctionAttribute> list = attributes.ToList ();
		list.Sort ((LlvmIrFunctionAttribute a, LlvmIrFunctionAttribute b) => a.Name.CompareTo (b.Name));

		return String.Join (" ", list.Select (a => a.Render ()));
	}

	public IEnumerator<LlvmIrFunctionAttribute> GetEnumerator () => attributes.GetEnumerator ();

	IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();

	public bool Equals (LlvmIrFunctionAttributeSet other)
	{
		if (other == null) {
			return false;
		}

		if (attributes.Count != other.attributes.Count) {
			return false;
		}

		foreach (LlvmIrFunctionAttribute attr in attributes) {
			if (!other.attributes.Contains (attr)) {
				return false;
			}
		}

		return true;
	}

	public override bool Equals (object obj)
	{
		var attrSet = obj as LlvmIrFunctionAttributeSet;
		if (attrSet == null) {
			return false;
		}

		return Equals (attrSet);
	}

	public override int GetHashCode()
	{
		int hc = 0;

		foreach (LlvmIrFunctionAttribute attr in attributes) {
			hc ^= attr?.GetHashCode () ?? 0;
		}

		return hc;
	}
}
