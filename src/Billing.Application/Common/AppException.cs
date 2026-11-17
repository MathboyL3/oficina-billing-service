namespace Billing.Application.Common;

public class AppException : Exception
{
    public AppException(string message) : base(message) { }
}

public class NotFoundException : AppException
{
    public NotFoundException(string entidade, object id) : base($"{entidade} não encontrado(a). ID: {id}.") { }
}

public class ConflictException : AppException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Gravação otimista rejeitada: outro processo alterou o documento depois da leitura.</summary>
public class ConcorrenciaException : ConflictException
{
    public ConcorrenciaException(string entidade, object id) : base($"{entidade} {id} foi alterado(a) por outra operação.") { }
}

/// <summary>Violação de índice único (reentrega concorrente ou duplicidade de OS).</summary>
public class DuplicidadeException : ConflictException
{
    public DuplicidadeException(string message) : base(message) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
}
