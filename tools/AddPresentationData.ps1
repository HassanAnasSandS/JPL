# Presentation data for JPL - teams + completed matches (not app seed).
# Uses existing Players only.

$ErrorActionPreference = "Stop"
$cs = "Data Source=DESKTOP-FM6EHSP;Initial Catalog=JPL;Integrated Security=True;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

function Exec([string]$sql) {
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = $sql
  $cmd.CommandTimeout = 120
  [void]$cmd.ExecuteNonQuery()
}

function Scalar([string]$sql) {
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = $sql
  return $cmd.ExecuteScalar()
}

# --- Player IDs ---
$p = @{
  Abdur   = "480c4d1c-b9d9-4883-af89-8a6e7f3b3d21"
  Ali     = "354eb3e1-4daa-4469-8ac5-217cc5319911"
  Amaan   = "c7eb6ac1-3dbd-44c1-b116-678dbef92179"
  Farhan  = "d22a838d-f938-4bb1-a765-b8ce51db5d50"
  Haris   = "f2d09ea7-dc19-413b-b3bb-af49ae1aae1e"
  Hasan   = "5ada3e64-b129-42db-b335-548e56783c81"
  Hassaan = "f1df0b7c-646e-4334-b860-e87c3884979d"
  Iqbal   = "4673a4cb-9a42-4ef7-bb7f-3079bcd69621"
  Naeem   = "69fde49b-89b6-42ef-b831-c17d8799dec0"
  Mohsin  = "71645869-5e70-47db-9114-8ab9d4b7ffca"
  Rehan   = "af5b4435-c281-4fe1-9031-217c9105f5df"
  Waleed  = "eb902a22-0135-485e-bf59-e85d0c6d9024"
}

$lionsId   = [guid]::NewGuid().ToString()
$falconsId = [guid]::NewGuid().ToString()
$now = [DateTimeOffset]::UtcNow.ToString("o")

$lions = @($p.Farhan, $p.Amaan, $p.Haris, $p.Hasan, $p.Abdur, $p.Ali)
$falcons = @($p.Mohsin, $p.Hassaan, $p.Iqbal, $p.Naeem, $p.Rehan, $p.Waleed)

Write-Host "Creating teams..."

Exec @"
IF NOT EXISTS (SELECT 1 FROM Teams WHERE Name = N'JPL Lions' AND IsDeleted = 0)
BEGIN
  INSERT INTO Teams (Id, Name, CaptainPlayerId, IsActive, IsDeleted, Version, CreatedAtUtc, UpdatedAtUtc)
  VALUES ('$lionsId', N'JPL Lions', '$($p.Farhan)', 1, 0, 1, '$now', '$now');
END
"@

Exec @"
IF NOT EXISTS (SELECT 1 FROM Teams WHERE Name = N'JPL Falcons' AND IsDeleted = 0)
BEGIN
  INSERT INTO Teams (Id, Name, CaptainPlayerId, IsActive, IsDeleted, Version, CreatedAtUtc, UpdatedAtUtc)
  VALUES ('$falconsId', N'JPL Falcons', '$($p.Mohsin)', 1, 0, 1, '$now', '$now');
END
"@

$lionsId = [string](Scalar "SELECT TOP 1 Id FROM Teams WHERE Name=N'JPL Lions' AND IsDeleted=0")
$falconsId = [string](Scalar "SELECT TOP 1 Id FROM Teams WHERE Name=N'JPL Falcons' AND IsDeleted=0")

Exec "DELETE FROM TeamPlayers WHERE TeamId IN ('$lionsId','$falconsId')"
foreach ($playerId in $lions) {
  Exec "INSERT INTO TeamPlayers (TeamId, PlayerId, JoinedAtUtc) VALUES ('$lionsId','$playerId','$now')"
}
foreach ($playerId in $falcons) {
  Exec "INSERT INTO TeamPlayers (TeamId, PlayerId, JoinedAtUtc) VALUES ('$falconsId','$playerId','$now')"
}

Write-Host "Lions=$lionsId Falcons=$falconsId"

function New-InningsDeliveries {
  param(
    [guid]$MatchId,
    [guid]$InningsId,
    [string[]]$Batters,
    [string[]]$Bowlers,
    [int]$Overs,
    [int]$Seed,
    [int]$TargetRuns
  )

  $rng = [Random]::new($Seed)
  $seq = 0
  $rows = New-Object System.Collections.Generic.List[string]
  $strikerIdx = 0
  $nonIdx = 1
  if ($nonIdx -ge $Batters.Count) { $nonIdx = 0 }
  $wickets = 0
  $runs = 0
  $nextBatter = 2

  for ($over = 0; $over -lt $Overs; $over++) {
    $bowler = $Bowlers[$over % $Bowlers.Count]
    $ballInOver = 0
    while ($ballInOver -lt 6) {
      if ($wickets -ge ($Batters.Count - 1)) { break }

      $seq++
      $ballInOver++
      $batter = $Batters[$strikerIdx]
      $non = $Batters[$nonIdx]

      $roll = $rng.Next(0, 100)
      $runsOff = 0
      $extra = 0
      $extraType = 0
      $isWicket = 0
      $wicketType = 0
      $dismissed = "NULL"
      $fielder = "NULL"
      $isLegal = 1

      # Lower wicket rate so 6-a-side sides often bat most overs.
      if ($roll -lt 3) {
        $extra = 1; $extraType = 1; $isLegal = 0; $ballInOver--
      }
      elseif ($roll -lt 5) {
        $extra = 1; $extraType = 2; $runsOff = $rng.Next(0, 2); $isLegal = 0; $ballInOver--
      }
      elseif ($roll -lt 9 -and $wickets -lt ($Batters.Count - 1)) {
        $isWicket = 1
        $wt = @(1,2,3,4,5)[$rng.Next(0,5)]
        $wicketType = $wt
        $dismissed = "'$batter'"
        if ($wt -in 2,4,5) {
          $fielderCand = $Bowlers[($over + 1) % $Bowlers.Count]
          if ($fielderCand -eq $bowler) {
            $fielderCand = $Bowlers[($over + 2) % $Bowlers.Count]
          }
          $fielder = "'$fielderCand'"
        }
        $wickets++
      }
      else {
        $bucket = $rng.Next(0, 100)
        if ($bucket -lt 22) { $runsOff = 0 }
        elseif ($bucket -lt 48) { $runsOff = 1 }
        elseif ($bucket -lt 66) { $runsOff = 2 }
        elseif ($bucket -lt 72) { $runsOff = 3 }
        elseif ($bucket -lt 90) { $runsOff = 4 }
        else { $runsOff = 6 }
      }

      $runs += $runsOff + $extra
      $id = [guid]::NewGuid().ToString()
      $created = [DateTimeOffset]::UtcNow.AddSeconds($seq).ToString("o")

      $rows.Add(@"
INSERT INTO Deliveries (Id, MatchId, InningsId, Sequence, OverNumber, BallInOver, BatterId, NonStrikerId, BowlerId, RunsOffBat, ExtraRuns, ExtraType, IsWicket, WicketType, DismissedPlayerId, FielderId, IsLegalDelivery, CreatedAtUtc, Version)
VALUES ('$id','$MatchId','$InningsId',$seq,$over,$ballInOver,'$batter','$non','$bowler',$runsOff,$extra,$extraType,$isWicket,$wicketType,$dismissed,$fielder,$isLegal,'$created',1);
"@)

      if ($isWicket -eq 1) {
        if ($nextBatter -lt $Batters.Count) {
          $strikerIdx = $nextBatter
          $nextBatter++
        }
      }
      elseif ($isLegal -eq 1 -and ($runsOff % 2 -eq 1)) {
        $tmp = $strikerIdx; $strikerIdx = $nonIdx; $nonIdx = $tmp
      }

      if ($TargetRuns -gt 0 -and $runs -ge $TargetRuns) { break }
    }
    if ($wickets -ge ($Batters.Count - 1)) { break }
    if ($TargetRuns -gt 0 -and $runs -ge $TargetRuns) { break }
    $tmp = $strikerIdx; $strikerIdx = $nonIdx; $nonIdx = $tmp
  }

  return @{ Sql = ($rows -join "`n"); Runs = $runs; Wickets = $wickets }
}

function Add-CompletedMatch {
  param(
    [string]$Title,
    [int]$OversLimit,
    [int]$Format,
    [string]$Venue,
    [datetime]$MatchDate,
    [int]$Seed
  )

  $existing = Scalar "SELECT COUNT(1) FROM Matches WHERE Title=N'$Title' AND IsDeleted=0"
  if ([int]$existing -gt 0) {
    Write-Host "Skip existing: $Title"
    return
  }

  $matchId = [guid]::NewGuid().ToString()
  $date = [DateTimeOffset]::new($MatchDate.ToUniversalTime()).ToString("o")
  $created = [DateTimeOffset]::UtcNow.ToString("o")
  $toss = $lionsId
  $aCap = $p.Farhan
  $bCap = $p.Mohsin

  Write-Host "Creating match: $Title ($OversLimit ov, format=$Format)"

  Exec @"
INSERT INTO Matches (Id, Title, TeamAId, TeamBId, TeamACaptainId, TeamBCaptainId, OversLimit, Format, MatchDateUtc, Venue, Status, TossWinnerTeamId, TossWinnerBatsFirst, Version, CreatedAtUtc, UpdatedAtUtc, IsDeleted)
VALUES ('$matchId', N'$Title', '$lionsId', '$falconsId', '$aCap', '$bCap', $OversLimit, $Format, '$date', N'$Venue', 2, '$toss', 1, 1, '$created', '$created', 0);
"@

  $inningsCount = if ($Format -eq 1) { 4 } else { 2 }
  $batOrder = @($lionsId, $falconsId, $lionsId, $falconsId)
  $bowlOrder = @($falconsId, $lionsId, $falconsId, $lionsId)
  $batSquad = @($lions, $falcons, $lions, $falcons)
  $bowlSquad = @($falcons, $lions, $falcons, $lions)

  $prevOppRuns = 0
  for ($i = 1; $i -le $inningsCount; $i++) {
    $innId = [guid]::NewGuid().ToString()
    $batTeam = $batOrder[$i - 1]
    $bowlTeam = $bowlOrder[$i - 1]
    $started = [DateTimeOffset]::UtcNow.AddHours($i).ToString("o")

    Exec @"
INSERT INTO Innings (Id, MatchId, InningsNumber, BattingTeamId, BowlingTeamId, IsCompleted, IsFollowOn, StartedAtUtc)
VALUES ('$innId','$matchId',$i,'$batTeam','$bowlTeam',1,0,'$started');
"@

    $target = 0
    if ($Format -eq 0 -and $i -eq 2) { $target = $prevOppRuns + 1 }
    if ($Format -eq 1 -and $i -eq 4) {
      $lionsTotal = [int](Scalar "SELECT ISNULL(SUM(d.RunsOffBat+d.ExtraRuns),0) FROM Deliveries d INNER JOIN Innings i ON i.Id=d.InningsId WHERE i.MatchId='$matchId' AND i.BattingTeamId='$lionsId'")
      $falconsSoFar = [int](Scalar "SELECT ISNULL(SUM(d.RunsOffBat+d.ExtraRuns),0) FROM Deliveries d INNER JOIN Innings i ON i.Id=d.InningsId WHERE i.MatchId='$matchId' AND i.BattingTeamId='$falconsId'")
      $target = ($lionsTotal - $falconsSoFar) + 1
      if ($target -lt 1) { $target = 1 }
    }

    $gen = New-InningsDeliveries -MatchId $matchId -InningsId $innId `
      -Batters $batSquad[$i-1] -Bowlers $bowlSquad[$i-1] `
      -Overs $OversLimit -Seed ($Seed + $i * 17) -TargetRuns $target

    $batch = $gen.Sql
    if ([string]::IsNullOrWhiteSpace($batch)) {
      Write-Host "  Warning: no deliveries for innings $i"
    } else {
      $statements = $batch -split "(?<=;)\r?\n" | Where-Object { $_.Trim().Length -gt 0 }
      $chunk = New-Object System.Collections.Generic.List[string]
      foreach ($st in $statements) {
        $chunk.Add($st)
        if ($chunk.Count -ge 25) {
          Exec ($chunk -join "`n")
          $chunk.Clear()
        }
      }
      if ($chunk.Count -gt 0) { Exec ($chunk -join "`n") }
    }

    Write-Host ("  Innings {0}: {1}/{2}" -f $i, $gen.Runs, $gen.Wickets)
    if ($i -eq 1) { $prevOppRuns = $gen.Runs }
  }
}

Add-CompletedMatch -Title "JPL T10 Cup - Lions vs Falcons" -OversLimit 10 -Format 0 -Venue "JPL Ground" -MatchDate (Get-Date).AddDays(-12) -Seed 101
Add-CompletedMatch -Title "JPL T20 League - Lions vs Falcons" -OversLimit 20 -Format 0 -Venue "City Stadium" -MatchDate (Get-Date).AddDays(-8) -Seed 202
Add-CompletedMatch -Title "JPL Double 10 - Lions vs Falcons" -OversLimit 10 -Format 1 -Venue "Academy Oval" -MatchDate (Get-Date).AddDays(-5) -Seed 303
Add-CompletedMatch -Title "JPL Double 20 Championship" -OversLimit 20 -Format 1 -Venue "National Arena" -MatchDate (Get-Date).AddDays(-2) -Seed 404

$teams = Scalar "SELECT COUNT(*) FROM Teams WHERE IsDeleted=0"
$matches = Scalar "SELECT COUNT(*) FROM Matches WHERE Status=2 AND IsDeleted=0"
$deliveries = Scalar "SELECT COUNT(*) FROM Deliveries"
Write-Host "DONE Teams=$teams CompletedMatches=$matches Deliveries=$deliveries"

$conn.Close()
