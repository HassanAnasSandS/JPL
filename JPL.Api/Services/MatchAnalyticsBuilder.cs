using JPL.Api.Models;

namespace JPL.Api.Services;

public sealed class ChartPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class ChartSeriesData
{
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#D4AF37";
    public List<ChartPoint> Points { get; set; } = new();
}

public sealed class BarPoint
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public int Wickets { get; set; }
    public string Color { get; set; } = "#1565C0";
}

public sealed class PhaseRow
{
    public string Name { get; set; } = "";
    public string Overs { get; set; } = "";
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int Balls { get; set; }
    public string RunRate { get; set; } = "-";
    public string ScoreLine => $"{Runs}/{Wickets}  {Overs}";
    public string RateLine => $"RR {RunRate}";
}

public sealed class PartnershipRow
{
    public string Pair { get; set; } = "";
    public int Runs { get; set; }
    public int Balls { get; set; }
    public string ForTeam { get; set; } = "";
    public string ScoreLine => $"{Runs} ({Balls})";
}

public sealed class InningsAnalytics
{
    public int Number { get; set; }
    public string TeamName { get; set; } = "";
    public string Color { get; set; } = "#D4AF37";
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int Dots { get; set; }
    public string RunRate { get; set; } = "-";
    public List<ChartPoint> Worm { get; set; } = new();
    public List<BarPoint> Manhattan { get; set; } = new();
    public List<ChartPoint> RunRateLine { get; set; } = new();
    public List<PhaseRow> Phases { get; set; } = new();
    public List<PartnershipRow> Partnerships { get; set; } = new();
    public List<BarPoint> Momentum { get; set; } = new();
}

public sealed class MatchAnalyticsReport
{
    public string Title { get; set; } = "";
    public string TeamAName { get; set; } = "";
    public string TeamBName { get; set; } = "";
    public string ResultText { get; set; } = "";
    public int OversLimit { get; set; }
    public bool IsChase { get; set; }
    public string ChaseText { get; set; } = "";
    public List<string> Insights { get; set; } = new();
    public List<ChartSeriesData> WormSeries { get; set; } = new();
    public List<ChartSeriesData> RunRateSeries { get; set; } = new();
    public List<InningsAnalytics> Innings { get; set; } = new();
}

public static class MatchAnalyticsBuilder
{
    private static readonly string[] Colors = ["#D4AF37", "#1565C0", "#C62828", "#15803D"];

    public static MatchAnalyticsReport Build(
        string title,
        string teamA,
        string teamB,
        string result,
        int oversLimit,
        IReadOnlyList<(int Number, string TeamName, IReadOnlyList<Delivery> Balls)> innings,
        IReadOnlyDictionary<Guid, string> names)
    {
        oversLimit = Math.Max(oversLimit, 1);
        var report = new MatchAnalyticsReport
        {
            Title = title,
            TeamAName = teamA,
            TeamBName = teamB,
            ResultText = result,
            OversLimit = oversLimit
        };

        for (var i = 0; i < innings.Count; i++)
        {
            var inn = innings[i];
            var color = Colors[i % Colors.Length];
            var built = BuildInnings(inn.Number, inn.TeamName, color, oversLimit, inn.Balls, names);
            report.Innings.Add(built);
            report.WormSeries.Add(new ChartSeriesData { Label = inn.TeamName, Color = color, Points = built.Worm });
            report.RunRateSeries.Add(new ChartSeriesData { Label = inn.TeamName, Color = color, Points = built.RunRateLine });
        }

        if (report.Innings.Count >= 2)
        {
            var first = report.Innings[0];
            var chase = report.Innings[^1];
            var target = first.Runs + 1;
            report.IsChase = true;
            var balls = innings[^1].Balls.Count(d => d.IsLegalDelivery);
            var remainBalls = Math.Max(0, oversLimit * 6 - balls);
            var need = Math.Max(0, target - chase.Runs);
            var req = remainBalls > 0 ? need * 6.0 / remainBalls : 0;
            report.ChaseText = remainBalls > 0
                ? $"Need {need} off {remainBalls} balls · req RR {req:0.00}"
                : chase.Runs >= target
                    ? $"{chase.TeamName} chased {target}"
                    : $"{chase.TeamName} {chase.Runs}/{chase.Wickets} chasing {target}";
        }

        report.Insights = BuildInsights(report, oversLimit);
        return report;
    }

    private static InningsAnalytics BuildInnings(
        int number,
        string team,
        string color,
        int oversLimit,
        IReadOnlyList<Delivery> balls,
        IReadOnlyDictionary<Guid, string> names)
    {
        var ordered = balls.OrderBy(d => d.Sequence).ToList();
        var inn = new InningsAnalytics { Number = number, TeamName = team, Color = color };
        var (ppEnd, deathStart) = PhaseBounds(oversLimit);

        var legal = 0;
        var total = 0;
        var wkts = 0;
        var overRuns = new Dictionary<int, (int Runs, int Wkts)>();
        var phases = new[]
        {
            new PhaseAcc("Powerplay", $"1–{ppEnd}"),
            new PhaseAcc("Middle", $"{ppEnd + 1}–{deathStart - 1}"),
            new PhaseAcc("Death", $"{deathStart}–{oversLimit}")
        };

        inn.Worm.Add(new ChartPoint());
        string Name(Guid id) => names.GetValueOrDefault(id, "Batter");

        Guid pairA = Guid.Empty, pairB = Guid.Empty;
        var pRuns = 0;
        var pBalls = 0;

        void ClosePartnership()
        {
            if (pairA == Guid.Empty || pRuns + pBalls == 0)
                return;
            inn.Partnerships.Add(new PartnershipRow
            {
                Pair = $"{Name(pairA)} / {Name(pairB)}",
                Runs = pRuns,
                Balls = pBalls,
                ForTeam = team
            });
            pRuns = 0;
            pBalls = 0;
        }

        foreach (var d in ordered)
        {
            var scored = d.RunsOffBat + d.ExtraRuns;
            total += scored;
            if (d.IsWicket) wkts++;
            if (d.RunsOffBat >= 4 && d.RunsOffBat < 6) inn.Fours++;
            if (d.RunsOffBat >= 6) inn.Sixes++;
            if (d.IsLegalDelivery && d.RunsOffBat == 0 && d.ExtraType == ExtraType.None && !d.IsWicket)
                inn.Dots++;

            if (pairA == Guid.Empty)
            {
                pairA = d.BatterId;
                pairB = d.NonStrikerId;
            }

            pRuns += scored;
            if (d.IsLegalDelivery) pBalls++;

            if (d.IsLegalDelivery)
            {
                legal++;
                var overNo = d.OverNumber;
                overRuns.TryGetValue(overNo, out var ov);
                overRuns[overNo] = (ov.Runs + scored, ov.Wkts + (d.IsWicket ? 1 : 0));

                var displayOver = overNo + 1;
                var phase = displayOver <= ppEnd ? 0 : displayOver >= deathStart ? 2 : 1;
                phases[phase].Runs += scored;
                phases[phase].Wkts += d.IsWicket ? 1 : 0;
                phases[phase].Balls++;
            }
            else
            {
                var overNo = Math.Max(d.OverNumber, 0);
                overRuns.TryGetValue(overNo, out var ov);
                overRuns[overNo] = (ov.Runs + scored, ov.Wkts + (d.IsWicket ? 1 : 0));
            }

            var x = legal / 6.0;
            inn.Worm.Add(new ChartPoint { X = x, Y = total });
            if (legal > 0)
                inn.RunRateLine.Add(new ChartPoint { X = x, Y = total * 6.0 / legal });

            if (d.IsWicket)
            {
                ClosePartnership();
                pairA = Guid.Empty;
                pairB = Guid.Empty;
            }
        }

        ClosePartnership();
        inn.Partnerships = inn.Partnerships.OrderByDescending(p => p.Runs).Take(6).ToList();

        inn.Runs = total;
        inn.Wickets = wkts;
        inn.RunRate = legal > 0 ? $"{total * 6.0 / legal:0.00}" : "-";

        var maxOver = overRuns.Count == 0 ? 0 : overRuns.Keys.Max();
        var innRr = legal > 0 ? total * 6.0 / legal : 0;
        for (var o = 0; o <= maxOver; o++)
        {
            overRuns.TryGetValue(o, out var ov);
            inn.Manhattan.Add(new BarPoint
            {
                Label = (o + 1).ToString(),
                Value = ov.Runs,
                Wickets = ov.Wkts,
                Color = ov.Runs >= innRr + 2 ? "#15803D" : ov.Runs + 0.01 < Math.Max(innRr - 2, 0) ? "#C62828" : color
            });
            inn.Momentum.Add(new BarPoint
            {
                Label = (o + 1).ToString(),
                Value = ov.Runs - innRr,
                Wickets = ov.Wkts,
                Color = ov.Runs >= innRr ? "#15803D" : "#C62828"
            });
        }

        inn.Phases = phases.Select(p => new PhaseRow
        {
            Name = p.Name,
            Overs = p.Overs,
            Runs = p.Runs,
            Wickets = p.Wkts,
            Balls = p.Balls,
            RunRate = p.Balls > 0 ? $"{p.Runs * 6.0 / p.Balls:0.00}" : "-"
        }).ToList();

        return inn;
    }

    private static List<string> BuildInsights(MatchAnalyticsReport report, int oversLimit)
    {
        var list = new List<string>();
        foreach (var inn in report.Innings)
        {
            if (inn.Phases.Count == 3 && inn.Runs > 0)
            {
                var death = inn.Phases[2];
                var pp = inn.Phases[0];
                if (death.Runs > 0)
                    list.Add($"{inn.TeamName} scored {Pct(death.Runs, inn.Runs)}% of runs in the death ({death.Overs} ov).");
                if (pp.Balls >= 6 && death.Balls >= 6 && death.RunRate != "-" && pp.RunRate != "-")
                {
                    if (double.TryParse(death.RunRate, out var drr) && double.TryParse(pp.RunRate, out var prr))
                    {
                        if (drr > prr + 1.5)
                            list.Add($"{inn.TeamName} accelerated: PP RR {pp.RunRate} → death RR {death.RunRate}.");
                        else if (prr > drr + 1.5)
                            list.Add($"{inn.TeamName} slowed after a strong powerplay ({pp.RunRate} → {death.RunRate}).");
                    }
                }
            }

            var best = inn.Partnerships.FirstOrDefault();
            if (best is { Runs: >= 15 })
                list.Add($"Best stand for {inn.TeamName}: {best.Pair} ({best.Runs} off {best.Balls}).");

            var spikes = inn.Manhattan.Count(b => b.Value >= 12);
            if (spikes > 0)
                list.Add($"{inn.TeamName} had {spikes} over{(spikes == 1 ? "" : "s")} of 12+.");

            if (inn.Dots > 0 && inn.Manhattan.Count > 0)
            {
                var legalEst = inn.Phases.Sum(p => p.Balls);
                if (legalEst >= 12)
                    list.Add($"Dot-ball pressure: {Pct(inn.Dots, legalEst)}% dots for {inn.TeamName}.");
            }
        }

        if (report.Innings.Count >= 2)
        {
            var a = report.Innings[0];
            var b = report.Innings[1];
            if (a.Phases.Count == 3 && b.Phases.Count == 3)
            {
                var aPp = a.Phases[0].Runs;
                var bPp = b.Phases[0].Runs;
                if (Math.Abs(aPp - bPp) >= 8)
                    list.Add(aPp > bPp
                        ? $"{a.TeamName} won the powerplay by {aPp - bPp} runs."
                        : $"{b.TeamName} won the powerplay by {bPp - aPp} runs.");
            }
        }

        if (list.Count == 0)
            list.Add(oversLimit <= 8
                ? "Short format — every over is a phase. Watch Manhattan spikes."
                : "Charts update live as each ball is scored.");
        return list.Take(6).ToList();
    }

    private static (int PpEnd, int DeathStart) PhaseBounds(int limit)
    {
        var pp = limit >= 20 ? 6 : limit >= 10 ? 3 : Math.Max(1, limit / 3);
        var death = limit >= 20 ? 5 : limit >= 10 ? 3 : Math.Max(1, limit / 3);
        var deathStart = Math.Max(pp + 1, limit - death + 1);
        return (pp, deathStart);
    }

    private static int Pct(int part, int whole)
        => whole <= 0 ? 0 : (int)Math.Round(part * 100.0 / whole);

    private sealed class PhaseAcc
    {
        public string Name { get; }
        public string Overs { get; }
        public int Runs;
        public int Wkts;
        public int Balls;
        public PhaseAcc(string name, string overs) { Name = name; Overs = overs; }
    }
}
