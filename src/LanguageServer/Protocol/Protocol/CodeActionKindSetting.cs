﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Roslyn.LanguageServer.Protocol;

using System.Text.Json.Serialization;

/// <summary>
/// Represents the code action kinds that are supported by the client.
/// <para>
/// See the <see href="https://microsoft.github.io/language-server-protocol/specifications/specification-current/#codeActionClientCapabilities">Language Server Protocol specification</see> for additional information.
/// </para>
/// </summary>
/// <remarks>Since LSP 3.8</remarks>
internal sealed class CodeActionKindSetting
{
    /// <summary>
    /// Gets or sets the code actions kinds the client supports.
    /// </summary>
    [JsonPropertyName("valueSet")]
    [JsonRequired]
    public CodeActionKind[] ValueSet
    {
        get;
        set;
    }
}
