using MongoDB.Driver;

namespace Billing.Infrastructure.Persistence;

public static class MongoExcecoes
{
    private const int CodigoChaveDuplicada = 11000;

    /// <summary>Violação de índice único (E11000), venha de escrita simples, em lote ou de comando.</summary>
    public static bool EhChaveDuplicada(this Exception e) => e switch
    {
        MongoWriteException w => w.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        MongoBulkWriteException b => b.WriteErrors.Any(x => x.Category == ServerErrorCategory.DuplicateKey),
        MongoCommandException c => c.Code == CodigoChaveDuplicada,
        _ => false
    };
}
