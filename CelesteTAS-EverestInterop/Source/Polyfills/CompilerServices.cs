// ReSharper disable once CheckNamespace
// ReSharper disable UnusedType.Global

using TAS.Utils;

namespace System.Runtime.CompilerServices {
    // Enable `string property { get; init; }`
    internal static class IsExternalInit;

    // Enable `required string x`
    public class RequiredMemberAttribute : Attribute;

    public class CompilerFeatureRequiredAttribute : Attribute {
        public CompilerFeatureRequiredAttribute(string name) {
        }
    }
}

namespace System.Diagnostics.CodeAnalysis {
    public class SetsRequiredMembersAttribute : Attribute;


    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    internal sealed class MemberNotNullWhenAttribute(bool returnValue, params string[] members) : Attribute {
        public bool ReturnValue { get; } = returnValue;
        public string[] Members { get; } = members;
    }
}
