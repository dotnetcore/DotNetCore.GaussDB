using System;
using GaussDB.Internal.Converters;
using GaussDB.Internal.Postgres;
using GaussDB.Properties;
using GaussDBTypes;

namespace GaussDB.Internal.ResolverFactories;

sealed class FullTextSearchTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    public static void CheckUnsupported<TBuilder>(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (type != typeof(object) && (dataTypeName == DataTypeNames.TsQuery || dataTypeName == DataTypeNames.TsVector))
            throw new NotSupportedException(
                string.Format(GaussDBStrings.FullTextSearchNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableFullTextSearch), typeof(TBuilder).Name));

        if (type is null)
            return;

        if (TypeInfoMappingCollection.IsArrayLikeType(type, out var elementType))
            type = elementType;

        if (type is { IsConstructedGenericType: true } && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            type = type.GetGenericArguments()[0];

        if (type == typeof(GaussDBTsVector) || typeof(GaussDBTsQuery).IsAssignableFrom(type))
            throw new NotSupportedException(
                string.Format(GaussDBStrings.FullTextSearchNotEnabled, nameof(GaussDBSlimDataSourceBuilder.EnableFullTextSearch), typeof(TBuilder).Name));
    }

    class Resolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection? _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // tsvector
            mappings.AddType<GaussDBTsVector>(DataTypeNames.TsVector,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsVectorConverter(options.TextEncoding)), isDefault: true);

            // tsquery
            mappings.AddType<GaussDBTsQuery>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQuery>(options.TextEncoding)), isDefault: true);
            mappings.AddType<GaussDBTsQueryEmpty>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryEmpty>(options.TextEncoding)));
            mappings.AddType<GaussDBTsQueryLexeme>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryLexeme>(options.TextEncoding)));
            mappings.AddType<GaussDBTsQueryNot>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryNot>(options.TextEncoding)));
            mappings.AddType<GaussDBTsQueryAnd>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryAnd>(options.TextEncoding)));
            mappings.AddType<GaussDBTsQueryOr>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryOr>(options.TextEncoding)));
            mappings.AddType<GaussDBTsQueryFollowedBy>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<GaussDBTsQueryFollowedBy>(options.TextEncoding)));

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
            // tsvector
            mappings.AddArrayType<GaussDBTsVector>(DataTypeNames.TsVector);

            // tsquery
            mappings.AddArrayType<GaussDBTsQuery>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryEmpty>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryLexeme>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryNot>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryAnd>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryOr>(DataTypeNames.TsQuery);
            mappings.AddArrayType<GaussDBTsQueryFollowedBy>(DataTypeNames.TsQuery);

            return mappings;
        }
    }
}
