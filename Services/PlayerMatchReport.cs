using JPL.Domain;

namespace JPL.Services;

public sealed class PlayerMatchReport
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string MatchTitle { get; set; } = string.Empty;
    public string BattingSummary { get; set; } = string.Empty;
    public string BowlingSummary { get; set; } = string.Empty;
    public string FieldingSummary { get; set; } = string.Empty;
    public bool HasBatting { get; set; }
    public bool HasBowling { get; set; }
    public bool HasFielding { get; set; }
    public List<PlayerMatchBallLine> BattingBalls { get; set; } = [];
    public List<PlayerMatchBallLine> BowlingBalls { get; set; } = [];
    public List<PlayerMatchBallLine> FieldingBalls { get; set; } = [];
}

public sealed class PlayerMatchBallLine
{
    public string OverText { get; set; } = string.Empty;
    public string Mark { get; set; } = string.Empty;
    public string Kind { get; set; } = "run";
    public string Detail { get; set; } = string.Empty;
    public string InningsLabel { get; set; } = string.Empty;
}

public static class PlayerMatchReportBuilder
{
    public static PlayerMatchReport Build(
        Guid playerId,
        string playerName,
        string teamName,
        string matchTitle,
        IReadOnlyList<(int Number, string BattingTeam, IReadOnlyList<Delivery> Balls)> innings,
        IReadOnlyDictionary<Guid, string> names)
    {
        string NameOf(Guid id) =>
            id == Guid.Empty ? "?" : names.GetValueOrDefault(id, "?");

        var report = new PlayerMatchReport
        {
            PlayerId = playerId,
            PlayerName = playerName,
            TeamName = teamName,
            MatchTitle = matchTitle
        };

        foreach (var inn in innings.OrderBy(i => i.Number))
        {
            var label = $"Inn {inn.Number} · {inn.BattingTeam}";
            foreach (var d in inn.Balls.OrderBy(x => x.Sequence))
            {
                var mark = ToMark(d);
                var over = $"{d.OverNumber}.{d.BallInOver}";
                if (d.BatterId == playerId)
                {
                    var extra = ExtraLabel(d);
                    var detail = d.IsWicket && d.DismissedPlayerId == playerId
                        ? $"vs {NameOf(d.BowlerId)} · {FormatDismissal(d, NameOf)}"
                        : string.IsNullOrEmpty(extra)
                            ? $"vs {NameOf(d.BowlerId)}"
                            : $"vs {NameOf(d.BowlerId)} · {extra}";
                    report.BattingBalls.Add(new PlayerMatchBallLine
                    {
                        OverText = over,
                        Mark = mark.Text,
                        Kind = mark.Kind,
                        Detail = detail,
                        InningsLabel = label
                    });
                }

                if (d.BowlerId == playerId)
                {
                    var extra = ExtraLabel(d);
                    var detail = d.IsWicket
                        ? $"to {NameOf(d.BatterId)} · {FormatDismissal(d, NameOf)}"
                        : string.IsNullOrEmpty(extra)
                            ? $"to {NameOf(d.BatterId)}"
                            : $"to {NameOf(d.BatterId)} · {extra}";
                    report.BowlingBalls.Add(new PlayerMatchBallLine
                    {
                        OverText = over,
                        Mark = mark.Text,
                        Kind = mark.Kind,
                        Detail = detail,
                        InningsLabel = label
                    });
                }

                if (d.FielderId == playerId && (d.IsWicket || d.IsDroppedCatch))
                {
                    string how;
                    string fieldMark;
                    string fieldKind;
                    if (d.IsDroppedCatch && !d.IsWicket)
                    {
                        how = $"dropped catch off {NameOf(d.BatterId)} ({NameOf(d.BowlerId)})";
                        fieldMark = "D";
                        fieldKind = "drop";
                    }
                    else
                    {
                        how = d.WicketType switch
                        {
                            WicketType.Caught => $"caught {NameOf(d.DismissedPlayerId ?? d.BatterId)} off {NameOf(d.BowlerId)}",
                            WicketType.RunOut => $"run out {NameOf(d.DismissedPlayerId ?? d.BatterId)}",
                            WicketType.Stumped => $"stumped {NameOf(d.DismissedPlayerId ?? d.BatterId)} off {NameOf(d.BowlerId)}",
                            _ => $"fielder · {NameOf(d.BatterId)}"
                        };
                        fieldMark = "W";
                        fieldKind = "wicket";
                    }

                    report.FieldingBalls.Add(new PlayerMatchBallLine
                    {
                        OverText = over,
                        Mark = fieldMark,
                        Kind = fieldKind,
                        Detail = how,
                        InningsLabel = label
                    });
                }
            }
        }

        var faced = innings.SelectMany(i => i.Balls).Where(d => d.BatterId == playerId).ToList();
        if (faced.Count > 0)
        {
            var runs = faced.Sum(d => d.RunsOffBat);
            var balls = faced.Count(d => d.IsLegalDelivery);
            var fours = faced.Count(d => d.RunsOffBat == 4);
            var sixes = faced.Count(d => d.RunsOffBat == 6);
            var sr = balls == 0 ? 0 : Math.Round(runs * 100.0 / balls, 1);
            var outs = innings.SelectMany(i => i.Balls)
                .Count(d => d.IsWicket && d.DismissedPlayerId == playerId);
            var how = outs == 0 ? "not out" : $"{outs} out";
            report.HasBatting = true;
            report.BattingSummary = $"{runs} ({balls})  4s {fours}  6s {sixes}  SR {sr:0.0}  ·  {how}";
        }
        else
        {
            report.BattingSummary = "Did not bat";
        }

        var bowled = innings.SelectMany(i => i.Balls).Where(d => d.BowlerId == playerId).ToList();
        if (bowled.Count > 0)
        {
            var legal = bowled.Count(d => d.IsLegalDelivery);
            var conceded = bowled.Sum(d => d.RunsOffBat + d.ExtraRuns);
            var wickets = bowled.Count(d =>
                d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None);
            var o = legal / 6;
            var b = legal % 6;
            var econ = legal == 0 ? 0 : Math.Round(conceded * 6.0 / legal, 2);
            report.HasBowling = true;
            report.BowlingSummary = $"{o}.{b}-{conceded}-{wickets}  Econ {econ:0.00}";
        }
        else
        {
            report.BowlingSummary = "Did not bowl";
        }

        var field = report.FieldingBalls;
        if (field.Count > 0)
        {
            var catches = field.Count(x => x.Detail.StartsWith("caught", StringComparison.Ordinal));
            var runOuts = field.Count(x => x.Detail.StartsWith("run out", StringComparison.Ordinal));
            var stumps = field.Count(x => x.Detail.StartsWith("stumped", StringComparison.Ordinal));
            var bits = new List<string>();
            if (catches > 0) bits.Add(catches == 1 ? "1 catch" : $"{catches} catches");
            if (runOuts > 0) bits.Add(runOuts == 1 ? "1 run out" : $"{runOuts} run outs");
            if (stumps > 0) bits.Add(stumps == 1 ? "1 stumping" : $"{stumps} stumpings");
            report.HasFielding = true;
            report.FieldingSummary = bits.Count == 0 ? $"{field.Count} dismissal(s)" : string.Join(" · ", bits);
        }
        else
        {
            report.FieldingSummary = "No catches / run outs / stumpings";
        }

        return report;
    }

    private static (string Text, string Kind) ToMark(Delivery d)
    {
        if (d.IsWicket)
            return ("W", "wicket");
        if (d.ExtraType == ExtraType.Wide)
            return ("wd", "extra");
        if (d.ExtraType == ExtraType.NoBall)
            return ("nb", "extra");
        var runs = d.RunsOffBat + (d.ExtraType is ExtraType.Bye or ExtraType.LegBye ? d.ExtraRuns : 0);
        if (runs >= 6)
            return ("6", "six");
        if (runs == 4)
            return ("4", "four");
        if (runs == 0)
            return ("•", "dot");
        return (runs.ToString(), "run");
    }

    private static string ExtraLabel(Delivery d) => d.ExtraType switch
    {
        ExtraType.Wide => d.ExtraRuns > 1 ? $"wide +{d.ExtraRuns}" : "wide",
        ExtraType.NoBall => d.ExtraRuns > 0 ? $"no-ball +{d.RunsOffBat + d.ExtraRuns}" : "no-ball",
        ExtraType.Bye => $"{d.ExtraRuns} bye",
        ExtraType.LegBye => $"{d.ExtraRuns} lb",
        _ => d.RunsOffBat > 0 ? $"{d.RunsOffBat} run{(d.RunsOffBat == 1 ? "" : "s")}" : string.Empty
    };

    private static string FormatDismissal(Delivery d, Func<Guid, string> nameOf)
    {
        var how = d.WicketType switch
        {
            WicketType.Bowled => "b",
            WicketType.Caught => d.FielderId is Guid f ? $"c {nameOf(f)} b" : "c b",
            WicketType.Lbw => "lbw b",
            WicketType.RunOut => d.FielderId is Guid f ? $"run out ({nameOf(f)})" : "run out",
            WicketType.Stumped => d.FielderId is Guid f ? $"st {nameOf(f)} b" : "st b",
            WicketType.HitWicket => "hit wicket b",
            WicketType.Retired => "retired",
            _ => "out"
        };
        if (d.WicketType is WicketType.RunOut or WicketType.Retired)
            return how;
        return $"{how} {nameOf(d.BowlerId)}";
    }
}
