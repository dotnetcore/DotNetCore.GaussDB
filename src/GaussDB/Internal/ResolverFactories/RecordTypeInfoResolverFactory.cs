using System;
using GaussDB.Internal.Converters;
using GaussDB.Internal.Postgres;
using GaussDB.Properties;

namespace GaussDB.Internal.ResolverFactories;

sealed class RecordTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    public static void CheckUnsupported<TBuilder>(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (type != typeof(object) && dataTypeName == DataTypeNames.Record)
        {
            throw new NotSupportedException(
                string.Format(
                    GaussDBStrings.RecordsNotEnabled,
                    nameof(GaussDBSlimDataSourceBuilder.EnableRecordsAsTuples),
                    typeof(TBuilder).Name,
                    nameof(GaussDBSlimDataSourceBuilder.EnableRecords)));
        }
    }

    class Resolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            mappings.AddType<object[]>(DataTypeNames.Record, static (options, mapping, _) =>
                    mapping.CreateInfo(options, new RecordConverter<object[]>(options), supportsWriting: false),
                MatchRequirement.DataTypeName);

            return mappings;
        }
    }

    sealed class ArrayResolver : Resolver, IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        new TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new(base.Mappings));

        public new PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            mappings.AddArrayType<object[]>(DataTypeNames.Record);

            return mappings;
        }
    }
}
