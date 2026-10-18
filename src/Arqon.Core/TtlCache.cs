namespace Arqon.Core;

/// <summary>Remembers an expensive lookup (adapter list, default route) for a short time; packet code may ask thousands of times a second.</summary>
public sealed class TtlCache<T>(Func<T> source, TimeSpan lifetime, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object _gate = new();
    private T? _value;
    private DateTimeOffset _expires = DateTimeOffset.MinValue;

    public T? Get()
    {
        lock (_gate)
        {
            var now = _clock();
            if (now < _expires) return _value;

            try { _value = source(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* keep the last good value */ }
            _expires = now + lifetime;
            return _value;
        }
    }
}
