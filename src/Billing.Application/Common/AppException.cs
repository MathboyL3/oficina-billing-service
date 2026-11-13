namespace Billing.Application.Common;

public class AppException : Exception
{
    public AppException(string message) : base(message) { }
}

public class NotFoundException : AppException
{
    public NotFoundException(string entidade, object id) : base($"{entidade} nÃ£o encontrado(a). ID: {id}.") { }
}

public class ConflictException : AppException
{
    public ConflictException(string message) : base(message) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
}
