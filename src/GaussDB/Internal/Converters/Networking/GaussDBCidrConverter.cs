using GaussDBTypes;

// ReSharper disable once CheckNamespace
namespace GaussDB.Internal.Converters;

sealed class GaussDBCidrConverter : PgBufferedConverter<GaussDBCidr>
{
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
        => CanConvertBufferedDefault(format, out bufferRequirements);

    public override Size GetSize(SizeContext context, GaussDBCidr value, ref object? writeState)
        => GaussDBInetConverter.GetSizeImpl(context, value.Address, ref writeState);

    protected override GaussDBCidr ReadCore(PgReader reader)
    {
        var (ip, netmask) = GaussDBInetConverter.ReadImpl(reader, shouldBeCidr: true);
        return new(ip, netmask);
    }

    protected override void WriteCore(PgWriter writer, GaussDBCidr value)
        => GaussDBInetConverter.WriteImpl(writer, (value.Address, value.Netmask), isCidr: true);
}
