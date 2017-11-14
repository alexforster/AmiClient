// Copyright © 2017 Alex Forster. All rights reserved.

using System;
using System.Reflection;

[assembly: AssemblyTitle("AmiClient")]
[assembly: AssemblyProduct("AmiClient")]
[assembly: AssemblyCopyright("Copyright © 2017 Alex Forster. All rights reserved.")]

[assembly: AssemblyVersion("1.0.0")]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
