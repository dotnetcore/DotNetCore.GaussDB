using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GaussDB.Internal;
using GaussDB.PostgresTypes;
using GaussDBTypes;

namespace GaussDB.Internal;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
/// Hacky temporary measure used by EFCore.PG to extract user-configured enum mappings. Accessed via reflection only.
/// </summary>
public sealed class HackyEnumTypeMapping
{
    public HackyEnumTypeMapping(Type enumClrType, string pgTypeName, IGaussDBNameTranslator nameTranslator)
    {
        EnumClrType = enumClrType;
        PgTypeName = pgTypeName;
        NameTranslator = nameTranslator;
    }

    public string PgTypeName { get; }
    public Type EnumClrType { get; }
    public IGaussDBNameTranslator NameTranslator { get; }
}
