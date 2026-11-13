using System.Reflection;
using Billing.Api.Middleware;
using Billing.Application.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace Billing.Tests.Integration;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<int> Executar(Exception ex)
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await new ErrorHandlingMiddleware(_ => throw ex, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(ctx);
        return ctx.Response.StatusCode;
    }

    private static MongoWriteException ChaveDuplicada()
    {
        // WriteError/ConnectionId não têm construtor público: monta pela reflexão só para o teste.
        var we = (WriteError)Activator.CreateInstance(typeof(WriteError), BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object?[] { ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key", null }, null)!;
        var conn = new MongoDB.Driver.Core.Connections.ConnectionId(
            new MongoDB.Driver.Core.Servers.ServerId(new MongoDB.Driver.Core.Clusters.ClusterId(1), new System.Net.DnsEndPoint("h", 1)));
        return (MongoWriteException)Activator.CreateInstance(typeof(MongoWriteException), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object?[] { conn, we, null, new Exception("dup") }, null)!;
    }

    [Fact]
    public async Task MongoWriteException_duplicate_key_vira_409()
        => (await Executar(ChaveDuplicada())).Should().Be(StatusCodes.Status409Conflict);

    [Fact]
    public async Task ConcorrenciaException_vira_409()
        => (await Executar(new ConcorrenciaException("Pagamento", Guid.NewGuid()))).Should().Be(StatusCodes.Status409Conflict);

    [Fact]
    public async Task Outra_excecao_continua_500()
        => (await Executar(new InvalidOperationException("x"))).Should().Be(StatusCodes.Status500InternalServerError);
}