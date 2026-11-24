using System.Linq.Expressions;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;

namespace Aaas.Application.Reservations;

/// <summary>
/// Filtres traduits en SQL par EF. <see cref="IsActive"/> est la traduction de <see cref="Reservation.IsActive"/> :
/// les deux doivent concorder (test d'intégration dédié, ADR 0005). Ne jamais réécrire la règle ailleurs.
/// </summary>
public static class ReservationQueries
{
    /// <summary>Occupe une place : Confirmée, ou En attente dont l'échéance n'est pas atteinte (ExpiresAt &gt; now).</summary>
    public static Expression<Func<Reservation, bool>> IsActive(DateTimeOffset now) =>
        r => r.Status == ReservationStatus.Confirmed || (r.Status == ReservationStatus.Pending && r.ExpiresAt > now);

    /// <summary>
    /// Section « À venir » de « Mes réservations » (US-D2, libelles-ecrans.md § 9) : active ET événement non terminé
    /// (un événement en cours reste « À venir »). « Historique » = la négation.
    /// </summary>
    public static Expression<Func<Reservation, bool>> IsCurrentForMember(DateTimeOffset now, IQueryable<Event> events)
    {
        Expression<Func<Reservation, bool>> eventNotEnded = r => events.Any(e => e.Id == r.EventId && e.EndsAt > now);
        return AndAlso(IsActive(now), eventNotEnded);
    }

    public static Expression<Func<Reservation, bool>> Not(Expression<Func<Reservation, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Expression.Lambda<Func<Reservation, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
    }

    /// <summary>Combine deux prédicats en un seul arbre (même paramètre) : EF n'accepte pas l'appel d'un délégué.</summary>
    private static Expression<Func<Reservation, bool>> AndAlso(
        Expression<Func<Reservation, bool>> left, Expression<Func<Reservation, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<Reservation, bool>>(Expression.AndAlso(left.Body, rightBody), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
