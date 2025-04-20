using System;
using System.Numerics;
using GaussDB.Internal.Converters;
using GaussDB.Internal.Postgres;
using GaussDB.PostgresTypes;
using GaussDB.Properties;
using GaussDB.Util;
using GaussDBTypes;
using static GaussDB.Internal.PgConverterFactory;

namespace GaussDB.Internal.ResolverFactories;

sealed partial class AdoTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateRangeResolver() => new RangeResolver();
    public override IPgTypeInfoResolver CreateRangeArrayResolver() => new RangeArrayResolver();

    public static void ThrowIfRangeUnsupported<TBuilder>(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        var kind = CheckRangeUnsupported(type, dataTypeName, options);
        switch (kind)
        {
        case PostgresTypeKind.Range when kind.Value.HasFlag(PostgresTypeKind.Array):
            throw new NotSupportedException(
                string.Format(GaussDBStrings.RangeArraysNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableArrays), typeof(TBuilder).Name));
        case PostgresTypeKind.Range:
            throw new NotSupportedException(
                string.Format(GaussDBStrings.RangesNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableRanges), typeof(TBuilder).Name));
        default:
            return;
        }
    }

    public static PostgresTypeKind? CheckRangeUnsupported(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        // Only trigger on well known data type names.
        var gaussDBDbType = dataTypeName?.ToGaussDBDbType();
        if (type != typeof(object))
        {
            if (gaussDBDbType?.HasFlag(GaussDBDbType.Range) != true && gaussDBDbType?.HasFlag(GaussDBDbType.Multirange) != true)
                return null;

            if (gaussDBDbType.Value.HasFlag(GaussDBDbType.Range))
                return dataTypeName?.IsArray == true
                    ? PostgresTypeKind.Array | PostgresTypeKind.Range
                    : PostgresTypeKind.Range;

            return dataTypeName?.IsArray == true
                ? PostgresTypeKind.Array | PostgresTypeKind.Multirange
                : PostgresTypeKind.Multirange;
        }

        if (type == typeof(object))
            return null;

        if (type is { IsConstructedGenericType: true } && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            type = type.GetGenericArguments()[0];

        if (type is { IsConstructedGenericType: true } && type.GetGenericTypeDefinition() == typeof(GaussDBRange<>))
        {
            type = type.GetGenericArguments()[0];
            var matchingArguments =
                new[]
                {
                    typeof(int), typeof(long), typeof(decimal), typeof(DateTime),
# if NET6_0_OR_GREATER
                    typeof(DateOnly)
#endif
                };

            // If we don't know more than the clr type, default to a Multirange kind over Array as they share the same types.
            foreach (var argument in matchingArguments)
                if (argument == type)
                    return PostgresTypeKind.Range;

            if (type.AssemblyQualifiedName == "System.Numerics.BigInteger,System.Runtime.Numerics")
                return PostgresTypeKind.Range;
        }

        return null;
    }

    class RangeResolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // numeric ranges
            mappings.AddStructType<GaussDBRange<int>>(DataTypeNames.Int4Range,
                static (options, mapping, _) => mapping.CreateInfo(options, CreateRangeConverter(new Int4Converter<int>(), options)),
                isDefault: true);
            mappings.AddStructType<GaussDBRange<long>>(DataTypeNames.Int8Range,
                static (options, mapping, _) => mapping.CreateInfo(options, CreateRangeConverter(new Int8Converter<long>(), options)),
                isDefault: true);
            mappings.AddStructType<GaussDBRange<decimal>>(DataTypeNames.NumRange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options, CreateRangeConverter(new DecimalNumericConverter<decimal>(), options)),
                isDefault: true);
            mappings.AddStructType<GaussDBRange<BigInteger>>(DataTypeNames.NumRange,
                static (options, mapping, _) => mapping.CreateInfo(options, CreateRangeConverter(new BigIntegerNumericConverter(), options)));

            // tsrange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddStructType<GaussDBRange<DateTime>>(DataTypeNames.TsRange,
                    static (options, mapping, _) => mapping.CreateInfo(options,
                        CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: true), options)),
                    isDefault: true);
            }
            else
            {
                mappings.AddResolverStructType<GaussDBRange<DateTime>>(DataTypeNames.TsRange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateRangeResolver(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzRange),
                            options.GetCanonicalTypeId(DataTypeNames.TsRange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch),
                    isDefault: true);
            }
            mappings.AddStructType<GaussDBRange<long>>(DataTypeNames.TsRange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options, CreateRangeConverter(new Int8Converter<long>(), options)));

            // tstzrange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddStructType<GaussDBRange<DateTime>>(DataTypeNames.TsTzRange,
                    static (options, mapping, _) => mapping.CreateInfo(options,
                        CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: false), options)),
                    isDefault: true);
                mappings.AddStructType<GaussDBRange<DateTimeOffset>>(DataTypeNames.TsTzRange,
                    static (options, mapping, _) => mapping.CreateInfo(options,
                        CreateRangeConverter(new LegacyDateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options)));
            }
            else
            {
                mappings.AddResolverStructType<GaussDBRange<DateTime>>(DataTypeNames.TsTzRange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateRangeResolver(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzRange),
                            options.GetCanonicalTypeId(DataTypeNames.TsRange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch),
                    isDefault: true);
                mappings.AddStructType<GaussDBRange<DateTimeOffset>>(DataTypeNames.TsTzRange,
                    static (options, mapping, _) => mapping.CreateInfo(options,
                        CreateRangeConverter(new DateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options)));
            }
            mappings.AddStructType<GaussDBRange<long>>(DataTypeNames.TsTzRange,
                static (options, mapping, _) => mapping.CreateInfo(options, CreateRangeConverter(new Int8Converter<long>(), options)));

            // daterange
            mappings.AddStructType<GaussDBRange<DateTime>>(DataTypeNames.DateRange,
                static (options, mapping, _) => mapping.CreateInfo(options,
                    CreateRangeConverter(new DateTimeDateConverter(options.EnableDateTimeInfinityConversions), options)),
                isDefault: true);
            mappings.AddStructType<GaussDBRange<int>>(DataTypeNames.DateRange,
                static (options, mapping, _) => mapping.CreateInfo(options, CreateRangeConverter(new Int4Converter<int>(), options)));
    #if NET6_0_OR_GREATER
            mappings.AddStructType<GaussDBRange<DateOnly>>(DataTypeNames.DateRange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options, CreateRangeConverter(new DateOnlyDateConverter(options.EnableDateTimeInfinityConversions), options)));
    #endif

            return mappings;
        }
    }

    sealed class RangeArrayResolver : RangeResolver, IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        new TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new(base.Mappings));

        public new PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // numeric ranges
            mappings.AddStructArrayType<GaussDBRange<int>>(DataTypeNames.Int4Range);
            mappings.AddStructArrayType<GaussDBRange<long>>(DataTypeNames.Int8Range);
            mappings.AddStructArrayType<GaussDBRange<decimal>>(DataTypeNames.NumRange);
            mappings.AddStructArrayType<GaussDBRange<BigInteger>>(DataTypeNames.NumRange);

            // tsrange
            if (Statics.LegacyTimestampBehavior)
                mappings.AddStructArrayType<GaussDBRange<DateTime>>(DataTypeNames.TsRange);
            else
                mappings.AddResolverStructArrayType<GaussDBRange<DateTime>>(DataTypeNames.TsRange);
            mappings.AddStructArrayType<GaussDBRange<long>>(DataTypeNames.TsRange);

            // tstzrange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddStructArrayType<GaussDBRange<DateTime>>(DataTypeNames.TsTzRange);
                mappings.AddStructArrayType<GaussDBRange<DateTimeOffset>>(DataTypeNames.TsTzRange);
            }
            else
            {
                mappings.AddResolverStructArrayType<GaussDBRange<DateTime>>(DataTypeNames.TsTzRange);
                mappings.AddStructArrayType<GaussDBRange<DateTimeOffset>>(DataTypeNames.TsTzRange);
            }
            mappings.AddStructArrayType<GaussDBRange<long>>(DataTypeNames.TsTzRange);

            // daterange
            mappings.AddStructArrayType<GaussDBRange<DateTime>>(DataTypeNames.DateRange);
            mappings.AddStructArrayType<GaussDBRange<int>>(DataTypeNames.DateRange);
#if NET6_0_OR_GREATER
            mappings.AddStructArrayType<GaussDBRange<DateOnly>>(DataTypeNames.DateRange);
#endif

            return mappings;
        }
    }
}
