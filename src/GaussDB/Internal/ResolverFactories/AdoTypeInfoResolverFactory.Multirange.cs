using System;
using System.Collections.Generic;
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
    public override IPgTypeInfoResolver CreateMultirangeResolver() => new MultirangeResolver();
    public override IPgTypeInfoResolver CreateMultirangeArrayResolver() => new MultirangeArrayResolver();

    public static void ThrowIfMultirangeUnsupported<TBuilder>(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        var kind = CheckMultirangeUnsupported(type, dataTypeName, options);
        switch (kind)
        {
        case PostgresTypeKind.Multirange when kind.Value.HasFlag(PostgresTypeKind.Array):
            throw new NotSupportedException(
                string.Format(GaussDBStrings.MultirangeArraysNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableArrays), typeof(TBuilder).Name));
        case PostgresTypeKind.Multirange:
            throw new NotSupportedException(
                string.Format(GaussDBStrings.MultirangesNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableMultiranges), typeof(TBuilder).Name));
        default:
            return;
        }
    }

    public static PostgresTypeKind? CheckMultirangeUnsupported(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        // Only trigger on well known data type names.
        var gaussDBDbType = dataTypeName?.ToGaussDBDbType();
        if (type != typeof(object))
        {
            if (gaussDBDbType?.HasFlag(GaussDBDbType.Multirange) != true)
                return null;

            return dataTypeName?.IsArray == true
                ? PostgresTypeKind.Array | PostgresTypeKind.Multirange
                : PostgresTypeKind.Multirange;
        }

        if (type == typeof(object))
            return null;

        if (!TypeInfoMappingCollection.IsArrayLikeType(type, out var elementType))
            return null;

        type = elementType;

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
                    return PostgresTypeKind.Multirange;

            if (type.AssemblyQualifiedName == "System.Numerics.BigInteger,System.Runtime.Numerics")
                return PostgresTypeKind.Multirange;
        }

        return null;
    }

    class MultirangeResolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => options.DatabaseInfo.SupportsMultirangeTypes ? Mappings.Find(type, dataTypeName, options) : null;

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // int4multirange
            mappings.AddType<GaussDBRange<int>[]>(DataTypeNames.Int4Multirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateArrayMultirangeConverter(CreateRangeConverter(new Int4Converter<int>(), options), options)),
                isDefault: true);
            mappings.AddType<List<GaussDBRange<int>>>(DataTypeNames.Int4Multirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateListMultirangeConverter(CreateRangeConverter(new Int4Converter<int>(), options), options)));

            // int8multirange
            mappings.AddType<GaussDBRange<long>[]>(DataTypeNames.Int8Multirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateArrayMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)),
                isDefault: true);
            mappings.AddType<List<GaussDBRange<long>>>(DataTypeNames.Int8Multirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateListMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)));

            // nummultirange
            mappings.AddType<GaussDBRange<decimal>[]>(DataTypeNames.NumMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateArrayMultirangeConverter(CreateRangeConverter(new DecimalNumericConverter<decimal>(), options), options)),
                isDefault: true);
            mappings.AddType<List<GaussDBRange<decimal>>>(DataTypeNames.NumMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateListMultirangeConverter(CreateRangeConverter(new DecimalNumericConverter<decimal>(), options), options)));

            // tsmultirange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddType<GaussDBRange<DateTime>[]>(DataTypeNames.TsMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: true),
                                options), options)),
                    isDefault: true);
                mappings.AddType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateListMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: true),
                                options), options)));
            }
            else
            {
                mappings.AddResolverType<GaussDBRange<DateTime>[]>(DataTypeNames.TsMultirange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateMultirangeResolver<GaussDBRange<DateTime>[], GaussDBRange<DateTime>>(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzMultirange),
                            options.GetCanonicalTypeId(DataTypeNames.TsMultirange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch),
                    isDefault: true);
                mappings.AddResolverType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsMultirange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateMultirangeResolver<List<GaussDBRange<DateTime>>, GaussDBRange<DateTime>>(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzMultirange),
                            options.GetCanonicalTypeId(DataTypeNames.TsMultirange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch));
            }

            mappings.AddType<GaussDBRange<long>[]>(DataTypeNames.TsMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateArrayMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)));
            mappings.AddType<List<GaussDBRange<long>>>(DataTypeNames.TsMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateListMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)));

            // tstzmultirange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddType<GaussDBRange<DateTime>[]>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: false),
                                options), options)),
                    isDefault: true);
                mappings.AddType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateListMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeConverter(options.EnableDateTimeInfinityConversions, timestamp: false),
                                options), options)));
                mappings.AddType<GaussDBRange<DateTimeOffset>[]>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options),
                            options)),
                    isDefault: true);
                mappings.AddType<List<GaussDBRange<DateTimeOffset>>>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateListMultirangeConverter(
                            CreateRangeConverter(new LegacyDateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options),
                            options)));
            }
            else
            {
                mappings.AddResolverType<GaussDBRange<DateTime>[]>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateMultirangeResolver<GaussDBRange<DateTime>[], GaussDBRange<DateTime>>(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzMultirange),
                            options.GetCanonicalTypeId(DataTypeNames.TsMultirange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch),
                    isDefault: true);
                mappings.AddResolverType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, dataTypeNameMatch) => mapping.CreateInfo(options,
                        DateTimeConverterResolver.CreateMultirangeResolver<List<GaussDBRange<DateTime>>, GaussDBRange<DateTime>>(options,
                            options.GetCanonicalTypeId(DataTypeNames.TsTzMultirange),
                            options.GetCanonicalTypeId(DataTypeNames.TsMultirange),
                            options.EnableDateTimeInfinityConversions), dataTypeNameMatch));
                mappings.AddType<GaussDBRange<DateTimeOffset>[]>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                            CreateRangeConverter(new DateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options), options)),
                    isDefault: true);
                mappings.AddType<List<GaussDBRange<DateTimeOffset>>>(DataTypeNames.TsTzMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateListMultirangeConverter(
                            CreateRangeConverter(new DateTimeOffsetConverter(options.EnableDateTimeInfinityConversions), options), options)));
            }

            mappings.AddType<GaussDBRange<long>[]>(DataTypeNames.TsTzMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateArrayMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)));
            mappings.AddType<List<GaussDBRange<long>>>(DataTypeNames.TsTzMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options,
                        CreateListMultirangeConverter(CreateRangeConverter(new Int8Converter<long>(), options), options)));

            // datemultirange
            mappings.AddType<GaussDBRange<DateTime>[]>(DataTypeNames.DateMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                        CreateRangeConverter(new DateTimeDateConverter(options.EnableDateTimeInfinityConversions), options), options)),
                isDefault: true);
            mappings.AddType<List<GaussDBRange<DateTime>>>(DataTypeNames.DateMultirange,
                static (options, mapping, _) =>
                    mapping.CreateInfo(options, CreateListMultirangeConverter(
                        CreateRangeConverter(new DateTimeDateConverter(options.EnableDateTimeInfinityConversions), options), options)));
    #if NET6_0_OR_GREATER
                mappings.AddType<GaussDBRange<DateOnly>[]>(DataTypeNames.DateMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateArrayMultirangeConverter(
                            CreateRangeConverter(new DateOnlyDateConverter(options.EnableDateTimeInfinityConversions), options), options)),
                    isDefault: true);
                mappings.AddType<List<GaussDBRange<DateOnly>>>(DataTypeNames.DateMultirange,
                    static (options, mapping, _) =>
                        mapping.CreateInfo(options, CreateListMultirangeConverter(
                            CreateRangeConverter(new DateOnlyDateConverter(options.EnableDateTimeInfinityConversions), options), options)));
    #endif

            return mappings;
        }
    }

    sealed class MultirangeArrayResolver : MultirangeResolver, IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        new TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new(base.Mappings));

        public new PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => options.DatabaseInfo.SupportsMultirangeTypes ? Mappings.Find(type, dataTypeName, options) : null;

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // int4multirange
            mappings.AddArrayType<GaussDBRange<int>[]>(DataTypeNames.Int4Multirange);
            mappings.AddArrayType<List<GaussDBRange<int>>>(DataTypeNames.Int4Multirange);

            // int8multirange
            mappings.AddArrayType<GaussDBRange<long>[]>(DataTypeNames.Int8Multirange);
            mappings.AddArrayType<List<GaussDBRange<long>>>(DataTypeNames.Int8Multirange);

            // nummultirange
            mappings.AddArrayType<GaussDBRange<decimal>[]>(DataTypeNames.NumMultirange);
            mappings.AddArrayType<List<GaussDBRange<decimal>>>(DataTypeNames.NumMultirange);

            // tsmultirange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddArrayType<GaussDBRange<DateTime>[]>(DataTypeNames.TsMultirange);
                mappings.AddArrayType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsMultirange);
            }
            else
            {
                mappings.AddResolverArrayType<GaussDBRange<DateTime>[]>(DataTypeNames.TsMultirange);
                mappings.AddResolverArrayType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsMultirange);
            }

            mappings.AddArrayType<GaussDBRange<long>[]>(DataTypeNames.TsMultirange);
            mappings.AddArrayType<List<GaussDBRange<long>>>(DataTypeNames.TsMultirange);

            // tstzmultirange
            if (Statics.LegacyTimestampBehavior)
            {
                mappings.AddArrayType<GaussDBRange<DateTime>[]>(DataTypeNames.TsTzMultirange);
                mappings.AddArrayType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsTzMultirange);
                mappings.AddArrayType<GaussDBRange<DateTimeOffset>[]>(DataTypeNames.TsTzMultirange);
                mappings.AddArrayType<List<GaussDBRange<DateTimeOffset>>>(DataTypeNames.TsTzMultirange);
            }
            else
            {
                mappings.AddResolverArrayType<GaussDBRange<DateTime>[]>(DataTypeNames.TsTzMultirange);
                mappings.AddResolverArrayType<List<GaussDBRange<DateTime>>>(DataTypeNames.TsTzMultirange);
                mappings.AddArrayType<GaussDBRange<DateTimeOffset>[]>(DataTypeNames.TsTzMultirange);
                mappings.AddArrayType<List<GaussDBRange<DateTimeOffset>>>(DataTypeNames.TsTzMultirange);
            }

            mappings.AddArrayType<GaussDBRange<long>[]>(DataTypeNames.TsTzMultirange);
            mappings.AddArrayType<List<GaussDBRange<long>>>(DataTypeNames.TsTzMultirange);

            // datemultirange
            mappings.AddArrayType<GaussDBRange<DateTime>[]>(DataTypeNames.DateMultirange);
            mappings.AddArrayType<List<GaussDBRange<DateTime>>>(DataTypeNames.DateMultirange);
    #if NET6_0_OR_GREATER
                mappings.AddArrayType<GaussDBRange<DateOnly>[]>(DataTypeNames.DateMultirange);
                mappings.AddArrayType<List<GaussDBRange<DateOnly>>>(DataTypeNames.DateMultirange);
    #endif

            return mappings;
        }
    }
}
