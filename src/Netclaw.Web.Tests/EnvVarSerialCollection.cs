// -----------------------------------------------------------------------
// <copyright file="EnvVarSerialCollection.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Xunit;

namespace Netclaw.Web.Tests;

// Tests in this collection touch process environment variables (NETCLAW_DAEMON_ENDPOINT).
// Running them in parallel with each other or with tests that resolve daemon
// endpoints would cause flakes from env-var bleed-through.
[CollectionDefinition(nameof(EnvVarSerialCollection), DisableParallelization = true)]
public sealed class EnvVarSerialCollection;
