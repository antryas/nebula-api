namespace Nebula.Application.Common;

public interface IClock
{
    /// <summary>Current time with <see cref="DateTimeKind.Utc"/>.</summary>
    DateTime UtcNow { get; }
}
