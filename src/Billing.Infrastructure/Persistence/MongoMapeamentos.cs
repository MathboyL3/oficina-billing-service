using System.Diagnostics.CodeAnalysis;
using Billing.Domain.Orcamentos;
using Billing.Domain.Pagamentos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Billing.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
public static class MongoMapeamentos
{
    private static int _registrado;

    public static void Registrar()
    {
        if (Interlocked.Exchange(ref _registrado, 1) == 1) return;
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.TryRegisterSerializer(new DecimalSerializer(BsonType.Decimal128));
        ConventionRegistry.Register("oficina", new ConventionPack
        {
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        }, _ => true);

        BsonClassMap.TryRegisterClassMap<Orcamento>(cm => { cm.AutoMap(); cm.MapIdMember(o => o.Id); cm.MapMember(o => o.Itens); });
        BsonClassMap.TryRegisterClassMap<ItemOrcamentoDominio>(cm => { cm.AutoMap(); cm.UnmapMember(i => i.Subtotal); cm.MapCreator(i => new ItemOrcamentoDominio(i.Tipo, i.ItemId, i.Descricao, i.ValorUnitario, i.Quantidade)); });
        BsonClassMap.TryRegisterClassMap<Pagamento>(cm => { cm.AutoMap(); cm.MapIdMember(p => p.Id); cm.UnmapMember(p => p.EstaFinal); });
    }
}
