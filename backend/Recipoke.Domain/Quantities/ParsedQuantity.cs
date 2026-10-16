namespace Recipoke.Domain.Quantities;

public sealed record ParsedQuantity(string Canonical, IReadOnlyList<Rational> Values);

