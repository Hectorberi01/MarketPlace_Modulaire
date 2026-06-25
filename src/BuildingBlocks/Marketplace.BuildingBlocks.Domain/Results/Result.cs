namespace Marketplace.BuildingBlocks.Domain.Results;

/// <summary>
/// Pattern Result : on modélise l'échec attendu comme une valeur, pas comme une
/// exception. Constant à toutes les phases de l'architecture (cf. dossier).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un succès ne peut pas porter d'erreur.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un échec doit porter une erreur.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>Result transportant une valeur en cas de succès.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("La valeur d'un Result en échec est inaccessible.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
