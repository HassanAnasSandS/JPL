using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;

namespace JPL.Api.Services;

/// <summary>
/// Soft-delete is refused when another table still references the row (FK / match history).
/// </summary>
internal static class DeleteConstraints
{
    public static async Task<string?> PlayerAsync(ApiDbContext db, Guid id, CancellationToken ct)
    {
        var inSquad = await (
            from tp in db.TeamPlayers
            join t in db.Teams on tp.TeamId equals t.Id
            where tp.PlayerId == id && !t.IsDeleted
            select tp.PlayerId).AnyAsync(ct).ConfigureAwait(false);
        if (inSquad)
            return "Cannot delete this player because they are in a team squad.";

        if (await db.Teams.AnyAsync(t => !t.IsDeleted && t.CaptainPlayerId == id, ct).ConfigureAwait(false))
            return "Cannot delete this player because they are a team captain.";

        if (await db.Matches.AnyAsync(m =>
                !m.IsDeleted && (
                    m.TeamACaptainId == id
                    || m.TeamBCaptainId == id
                    || m.PlayerOfTheMatchPlayerId == id), ct).ConfigureAwait(false))
            return "Cannot delete this player because they are linked to a match.";

        if (await db.Deliveries.AnyAsync(d =>
                d.BatterId == id
                || d.NonStrikerId == id
                || d.BowlerId == id
                || d.DismissedPlayerId == id
                || d.FielderId == id, ct).ConfigureAwait(false))
            return "Cannot delete this player because they appear in match deliveries.";

        if (await db.NetDeliveries.AnyAsync(d =>
                d.BatterId == id
                || d.NonStrikerId == id
                || d.BowlerId == id
                || d.DismissedPlayerId == id
                || d.FielderId == id, ct).ConfigureAwait(false))
            return "Cannot delete this player because they appear in nets sessions.";

        var auctionCaptain = await (
            from c in db.AuctionCaptains
            join a in db.Auctions on c.AuctionId equals a.Id
            where c.PlayerId == id && !a.IsDeleted
            select c.PlayerId).AnyAsync(ct).ConfigureAwait(false);
        if (auctionCaptain)
            return "Cannot delete this player because they are an auction captain.";

        var auctionLot = await (
            from l in db.AuctionLots
            join a in db.Auctions on l.AuctionId equals a.Id
            where !a.IsDeleted && (l.PlayerId == id || l.SoldToCaptainPlayerId == id)
            select l.Id).AnyAsync(ct).ConfigureAwait(false);
        if (auctionLot)
            return "Cannot delete this player because they are in an auction.";

        if (await db.VenueBookings.AnyAsync(b => !b.IsDeleted && b.BookedByPlayerId == id, ct).ConfigureAwait(false))
            return "Cannot delete this player because they have venue bookings.";

        var participant = await (
            from p in db.VenueBookingParticipants
            join b in db.VenueBookings on p.VenueBookingId equals b.Id
            where p.PlayerId == id && !b.IsDeleted
            select p.PlayerId).AnyAsync(ct).ConfigureAwait(false);
        if (participant)
            return "Cannot delete this player because they are a venue booking participant.";

        var key = id.ToString("D");
        if (await db.Matches.AnyAsync(m =>
                !m.IsDeleted
                && m.SquadRosterJson != null
                && m.SquadRosterJson.Contains(key), ct).ConfigureAwait(false))
            return "Cannot delete this player because they are in a match squad.";

        if (await db.NetSessions.AnyAsync(s =>
                !s.IsDeleted
                && s.PlayerRosterJson != null
                && s.PlayerRosterJson.Contains(key), ct).ConfigureAwait(false))
            return "Cannot delete this player because they are in a nets roster.";

        return null;
    }

    public static async Task<string?> TeamAsync(ApiDbContext db, Guid id, CancellationToken ct)
    {
        if (await db.TeamPlayers.AnyAsync(tp => tp.TeamId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it still has players. Remove them from the squad first.";

        if (await db.Matches.AnyAsync(m =>
                !m.IsDeleted && (
                    m.TeamAId == id
                    || m.TeamBId == id
                    || m.TossWinnerTeamId == id), ct).ConfigureAwait(false))
            return "Cannot delete this team because it is used in a match.";

        if (await db.Innings.AnyAsync(i => i.BattingTeamId == id || i.BowlingTeamId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it is used in a match innings.";

        if (await db.TournamentTeams.AnyAsync(t => t.TeamId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it is in a tournament.";

        if (await db.Tournaments.AnyAsync(t => !t.IsDeleted && t.WinnerTeamId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it won a tournament.";

        if (await db.TournamentFixtures.AnyAsync(f => f.TeamAId == id || f.TeamBId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it is in a tournament fixture.";

        if (await db.Series.AnyAsync(s =>
                !s.IsDeleted && (s.TeamAId == id || s.TeamBId == id || s.WinnerTeamId == id), ct).ConfigureAwait(false))
            return "Cannot delete this team because it is in a series.";

        if (await db.SeriesFixtures.AnyAsync(f => f.TeamAId == id || f.TeamBId == id, ct).ConfigureAwait(false))
            return "Cannot delete this team because it is in a series fixture.";

        return null;
    }

    public static async Task<string?> VenueAsync(ApiDbContext db, Guid id, CancellationToken ct)
    {
        if (await db.VenueBookings.AnyAsync(b => !b.IsDeleted && b.VenueId == id, ct).ConfigureAwait(false))
            return "Cannot delete this venue because it has bookings.";

        if (await db.Tournaments.AnyAsync(t => !t.IsDeleted && t.VenueId == id, ct).ConfigureAwait(false))
            return "Cannot delete this venue because it is used by a tournament.";

        if (await db.Series.AnyAsync(s => !s.IsDeleted && s.VenueId == id, ct).ConfigureAwait(false))
            return "Cannot delete this venue because it is used by a series.";

        return null;
    }
}
