-- Repair: 26th July (1st Match) — Farhan Falcons innings 1
-- Broken undo removed the last 7 legal balls (over 8 ball 6 + all of over 9).
-- Restores: total 76/3 (8.5 ov) -> 103/3 (10 ov); Farhan Rehan 28->47*, Mohsin Anas 18->26*.
-- Backup taken first: JPL_before_fix_20260726.bak

SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @inn  uniqueidentifier = '7B0276FC-A07F-4CA0-A7BE-4359BEBE4926';
DECLARE @mat  uniqueidentifier = '408534AB-B63F-431B-A6E6-31D4B8A89356';
DECLARE @FR   uniqueidentifier = '3DE22592-E809-420C-A116-5A195AFF8345'; -- Farhan Rehan
DECLARE @MA   uniqueidentifier = 'B8436AEA-A240-49BB-8421-5836B7980618'; -- Mohsin Anas
DECLARE @HAS  uniqueidentifier = 'FBADE052-C9C9-4075-91AE-DBC04F9082CD'; -- Hasan Anas (bowler ov 8)
DECLARE @HI   uniqueidentifier = '45639D6F-163A-450D-B982-DD9F1C9CF964'; -- Haris Imran (bowler ov 9)
DECLARE @now  datetimeoffset = SYSDATETIMEOFFSET();

-- Guard: innings must be in the known broken state (55 rows, max seq 55)
IF (SELECT COUNT(*) FROM Deliveries WHERE InningsId = @inn) <> 55
   OR (SELECT MAX(Sequence) FROM Deliveries WHERE InningsId = @inn) <> 55
BEGIN
    ROLLBACK;
    THROW 50001, 'Innings not in expected state (55 rows / max seq 55). Aborting.', 1;
END

INSERT INTO Deliveries
    (Id, MatchId, InningsId, Sequence, OverNumber, BallInOver,
     BatterId, NonStrikerId, BowlerId,
     RunsOffBat, ExtraRuns, ExtraType, IsWicket, WicketType,
     DismissedPlayerId, FielderId, IsLegalDelivery, CreatedAtUtc, Version)
VALUES
    (NEWID(), @mat, @inn, 56, 8, 6, @MA, @FR, @HAS, 6, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 57, 9, 1, @FR, @MA, @HI,  6, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 58, 9, 2, @FR, @MA, @HI,  6, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 59, 9, 3, @FR, @MA, @HI,  6, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 60, 9, 4, @FR, @MA, @HI,  1, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 61, 9, 5, @MA, @FR, @HI,  2, 0, 0, 0, 0, NULL, NULL, 1, @now, 1),
    (NEWID(), @mat, @inn, 62, 9, 6, @MA, @FR, @HI,  0, 0, 0, 0, 0, NULL, NULL, 1, @now, 1);

-- Verify final state before committing
DECLARE @total int, @wkts int, @legal int, @frRuns int, @maRuns int;
SELECT @total = SUM(RunsOffBat + ExtraRuns),
       @wkts  = SUM(CASE WHEN IsWicket = 1 THEN 1 ELSE 0 END),
       @legal = SUM(CASE WHEN IsLegalDelivery = 1 THEN 1 ELSE 0 END)
FROM Deliveries WHERE InningsId = @inn;
SELECT @frRuns = SUM(RunsOffBat) FROM Deliveries WHERE InningsId = @inn AND BatterId = @FR;
SELECT @maRuns = SUM(RunsOffBat) FROM Deliveries WHERE InningsId = @inn AND BatterId = @MA;

IF @total <> 103 OR @wkts <> 3 OR @legal <> 60 OR @frRuns <> 47 OR @maRuns <> 26
BEGIN
    ROLLBACK;
    THROW 50002, 'Verification failed after insert. Rolled back.', 1;
END

UPDATE Matches SET Version = Version + 1, UpdatedAtUtc = @now WHERE Id = @mat;

COMMIT;
PRINT 'FIX APPLIED: Innings 1 = 103/3 in 10 overs; Farhan Rehan 47*, Mohsin Anas 26*.';
