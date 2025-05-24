using JetBrains.Annotations;
using System;
using TAS.Utils;

namespace TAS.Module;

/// Invokes the target method when the module is loaded
[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class LoadAttribute(int priority = 0) : EventAttribute(priority);

/// Invokes the target method when the module's content is loaded
[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class LoadContentAttribute : Attribute;

/// Invokes the target method when the module is unloaded
[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class UnloadAttribute(int priority = 0) : EventAttribute(priority);

/// Invokes the target method when the module is initialized
[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class InitializeAttribute(int priority = 0) : EventAttribute(priority);
