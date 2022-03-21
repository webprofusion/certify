// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Identifiers refer to standard string names", Scope = "module")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Ignore culture for strign lower", Scope = "module")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "No requirement to avoid reserved words in type names", Scope = "module")]
