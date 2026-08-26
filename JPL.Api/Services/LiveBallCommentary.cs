using JPL.Api.Models;

namespace JPL.Api.Services;

/// <summary>
/// Ball-by-ball live commentary (English + Urdu). Every line names the bowler,
/// the batter who faced, and what happened on that ball.
/// </summary>
public static class LiveBallCommentary
{
    public sealed record Line(string English, string Urdu, string Cue);

    public static Line? For(
        Delivery last,
        IReadOnlyList<Delivery> inningsBalls,
        string batter,
        string bowler,
        string? fielder,
        string battingTeam,
        int inningsRuns,
        int inningsWkts)
    {
        if (last.Sequence <= 0)
            return null;

        batter = Person(batter, "the batter");
        bowler = Person(bowler, "the bowler");
        battingTeam = Person(battingTeam, "the batting side");
        var seed = Hash(last.Id, last.Sequence);
        var overBall = $"{last.OverNumber}.{Math.Max(last.BallInOver, 1)}";
        var score = $"{inningsRuns}/{inningsWkts}";

        var batterBalls = inningsBalls.Where(d => d.BatterId == last.BatterId).ToList();
        var batterRuns = batterBalls.Sum(d => d.RunsOffBat);
        var batterFaced = batterBalls.Count;
        var hitFifty = last.RunsOffBat > 0 && batterRuns >= 50 && batterRuns - last.RunsOffBat < 50;
        var hitHundred = last.RunsOffBat > 0 && batterRuns >= 100 && batterRuns - last.RunsOffBat < 100;
        var overDone = last.IsLegalDelivery && last.BallInOver >= 6;

        string En(string happened) =>
            $"{bowler} bowls to {batter}. {happened}";
        string Ur(string happened) =>
            $"{bowler} نے {batter} کو گیند کی۔ {happened}";

        if (last.IsWicket)
        {
            var how = HowOut(last, fielder);
            return Pick(seed,
                En($"{batter} is out, {how.En}. {battingTeam} {score} at {overBall}."),
                En($"Wicket. {how.En}. {battingTeam} now {score}."),
                Ur($"{batter} آؤٹ، {how.Ur}۔ {battingTeam} {overBall} پر {score}۔"),
                Ur($"وکٹ۔ {how.Ur}۔ {battingTeam} {score} پر۔"),
                "WICKET");
        }

        if (last.ExtraType == ExtraType.Wide)
            return Pick(seed,
                En($"Wide. Extra run. {battingTeam} {score} at {overBall}."),
                En($"That is a wide. {batter} leaves it. {score}."),
                Ur($"وائیڈ۔ اضافی رن۔ {battingTeam} {overBall} پر {score}۔"),
                Ur($"وائیڈ۔ {batter} نے گیند چھوڑ دی۔ {score}۔"),
                "WIDE");

        if (last.ExtraType == ExtraType.NoBall)
        {
            var offBat = last.RunsOffBat;
            if (offBat >= 6)
                return Pick(seed,
                    En($"No-ball, and {batter} hits SIX. Free hit next. {score}."),
                    En($"Overstep, and a six off the bat. {battingTeam} {score}."),
                    Ur($"نو بال، اور {batter} نے چھکا لگایا۔ فری ہٹ۔ {score}۔"),
                    Ur($"نو بال پر چھکا۔ {battingTeam} {score}۔"),
                    "NO BALL · SIX");
            if (offBat >= 4)
                return Pick(seed,
                    En($"No-ball, and {batter} hits FOUR. Free hit next. {score}."),
                    En($"Overstep, and a boundary. {battingTeam} {score} at {overBall}."),
                    Ur($"نو بال، اور {batter} نے چوکا لگایا۔ فری ہٹ۔ {score}۔"),
                    Ur($"نو بال پر باؤنڈری۔ {overBall} پر {score}۔"),
                    "NO BALL · FOUR");
            return Pick(seed,
                En($"No-ball. Free hit next. {battingTeam} {score} at {overBall}."),
                En($"Overstep from {bowler}. Extra, free hit for {batter}."),
                Ur($"نو بال۔ فری ہٹ۔ {battingTeam} {overBall} پر {score}۔"),
                Ur($"نو بال۔ {batter} کو فری ہٹ۔ {score}۔"),
                "NO BALL");
        }

        if (last.ExtraType == ExtraType.Bye)
        {
            var n = Math.Max(last.ExtraRuns, 1);
            return Pick(seed,
                En($"Byes, {n} run{(n == 1 ? "" : "s")}. {batter} did not hit it. {score} at {overBall}."),
                En($"Past the keeper, byes. {battingTeam} {score}."),
                Ur($"بائیز، {n} رن۔ {batter} نے نہیں لگائی۔ {overBall} پر {score}۔"),
                Ur($"کیپر کے پاس سے بائیز۔ {battingTeam} {score}۔"),
                "BYES");
        }

        if (last.ExtraType == ExtraType.LegBye)
            return Pick(seed,
                En($"Leg bye. Off the pad, they run. {score} at {overBall}."),
                En($"Leg byes. {battingTeam} move to {score}."),
                Ur($"لیگ بائی۔ پیڈ سے لگ کر رن۔ {overBall} پر {score}۔"),
                Ur($"لیگ بائیز۔ {battingTeam} {score} پر۔"),
                "LEG BYE");

        var runs = last.RunsOffBat;
        if (runs >= 6)
        {
            var extra = hitHundred
                ? $" That is a hundred for {batter}, {batterRuns} off {batterFaced}."
                : hitFifty
                    ? $" That is fifty for {batter}, {batterRuns} off {batterFaced}."
                    : "";
            var extraUr = hitHundred
                ? $" {batter} کی سنچری، {batterFaced} گیندوں پر {batterRuns}۔"
                : hitFifty
                    ? $" {batter} کی پچاس، {batterFaced} گیندوں پر {batterRuns}۔"
                    : "";
            var cue = hitHundred ? "SIX · 100" : hitFifty ? "SIX · 50" : "SIX";
            return Pick(seed,
                En($"Six. {batter} hits it over the boundary.{extra} {score} at {overBall}."),
                En($"Maximum. Six runs.{extra} {battingTeam} {score}."),
                Ur($"چھکا۔ {batter} نے باؤنڈری کے پار بھیجا۔{extraUr} {overBall} پر {score}۔"),
                Ur($"چھ رنز۔{extraUr} {battingTeam} {score}۔"),
                cue);
        }

        if (runs >= 4)
        {
            var extra = hitHundred
                ? $" That is a hundred for {batter}, {batterRuns} off {batterFaced}."
                : hitFifty
                    ? $" That is fifty for {batter}, {batterRuns} off {batterFaced}."
                    : "";
            var extraUr = hitHundred
                ? $" {batter} کی سنچری، {batterFaced} گیندوں پر {batterRuns}۔"
                : hitFifty
                    ? $" {batter} کی پچاس، {batterFaced} گیندوں پر {batterRuns}۔"
                    : "";
            var cue = hitHundred ? "FOUR · 100" : hitFifty ? "FOUR · 50" : "FOUR";
            return Pick(seed,
                En($"Four. {batter} finds the boundary.{extra} {score} at {overBall}."),
                En($"Boundary. Four runs.{extra} {battingTeam} {score}."),
                Ur($"چوکا۔ {batter} نے باؤنڈری نکالی۔{extraUr} {overBall} پر {score}۔"),
                Ur($"چار رنز۔{extraUr} {battingTeam} {score}۔"),
                cue);
        }

        if (hitHundred)
            return Pick(seed,
                En($"{runs} run{(runs == 1 ? "" : "s")}, and a hundred for {batter}, {batterRuns} off {batterFaced}. {score}."),
                En($"Century for {batter}. {battingTeam} {score} at {overBall}."),
                Ur($"{UrduRuns(runs)}، اور {batter} کی سنچری، {batterFaced} گیندوں پر {batterRuns}۔ {score}۔"),
                Ur($"{batter} کی سنچری۔ {overBall} پر {score}۔"),
                "100");

        if (hitFifty)
            return Pick(seed,
                En($"{runs} run{(runs == 1 ? "" : "s")}, and fifty for {batter}, {batterRuns} off {batterFaced}. {score}."),
                En($"Half-century for {batter}. {score} at {overBall}."),
                Ur($"{UrduRuns(runs)}، اور {batter} کی پچاس، {batterFaced} گیندوں پر {batterRuns}۔ {score}۔"),
                Ur($"{batter} کی نصف سنچری۔ {overBall} پر {score}۔"),
                "50");

        if (runs >= 1)
        {
            var end = overDone ? " That is the end of the over." : "";
            var endUr = overDone ? " اوور ختم۔" : "";
            return Pick(seed,
                En($"{batter} takes {runs} run{(runs == 1 ? "" : "s")}.{end} {score} at {overBall}."),
                En($"{runs} run{(runs == 1 ? "" : "s")} off the bat.{end} {battingTeam} {score}."),
                Ur($"{batter} نے {UrduRuns(runs)} لیے۔{endUr} {overBall} پر {score}۔"),
                Ur($"بلے سے {UrduRuns(runs)}۔{endUr} {battingTeam} {score}۔"),
                runs.ToString());
        }

        var dotEnd = overDone ? " End of the over." : "";
        var dotEndUr = overDone ? " اوور ختم۔" : "";
        return Pick(seed,
            En($"Dot ball. No run.{dotEnd} {score} at {overBall}."),
            En($"{batter} defends. No run.{dotEnd} {battingTeam} {score}."),
            Ur($"ڈاٹ بال۔ کوئی رن نہیں۔{dotEndUr} {overBall} پر {score}۔"),
            Ur($"{batter} نے دفاع کیا۔ رن نہیں۔{dotEndUr} {battingTeam} {score}۔"),
            "DOT");
    }

    private static string Person(string? name, string fallback)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "?" or "-" or ".")
            return fallback;
        return name;
    }

    private static (string En, string Ur) HowOut(Delivery last, string? fielder)
    {
        var f = string.IsNullOrWhiteSpace(fielder) ? null : fielder.Trim();
        return last.WicketType switch
        {
            WicketType.Bowled => ("bowled", "بولڈ"),
            WicketType.Caught => (f is null ? "caught" : $"caught by {f}", f is null ? "کیچ" : $"{f} کے ہاتھوں کیچ"),
            WicketType.Lbw => ("LBW", "ایل بی ڈبلیو"),
            WicketType.RunOut => (f is null ? "run out" : $"run out by {f}", f is null ? "رن آؤٹ" : $"{f} سے رن آؤٹ"),
            WicketType.Stumped => ("stumped", "سٹمپڈ"),
            WicketType.HitWicket => ("hit wicket", "ہٹ وکٹ"),
            WicketType.Retired => ("retired", "ریٹائرڈ"),
            _ => ("out", "آؤٹ")
        };
    }

    private static string UrduRuns(int runs) => runs switch
    {
        1 => "ایک رن",
        2 => "دو رنز",
        3 => "تین رنز",
        4 => "چار رنز",
        5 => "پانچ رنز",
        _ => $"{runs} رنز"
    };

    private static Line Pick(int seed, string en1, string en2, string ur1, string ur2, string cue)
        => (seed & 1) == 0 ? new Line(en1, ur1, cue) : new Line(en2, ur2, cue);

    private static int Hash(Guid id, int sequence)
    {
        unchecked
        {
            return id.GetHashCode() * 397 + sequence;
        }
    }
}
