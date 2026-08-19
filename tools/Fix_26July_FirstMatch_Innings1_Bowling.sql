-- Innings 1 bowling: Amaan Rehan should have 2 overs (not Haris Imran for over 9).
-- Hasan Anas already has 2.0 (overs 4 + 8) after the earlier batting repair.
-- Over 9 = 21 runs, currently Haris Imran -> reassign to Amaan Rehan.
-- Total stays 103.

SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @inn uniqueidentifier = '7B0276FC-A07F-4CA0-A7BE-4359BEBE4926';
DECLARE @mat uniqueidentifier = '408534AB-B63F-431B-A6E6-31D4B8A89356';
DECLARE @AR  uniqueidentifier = '308C7F44-71F4-4A83-BDC4-88410616FF67'; -- Amaan Rehan
DECLARE @HI  uniqueidentifier = '45639D6F-163A-450D-B982-DD9F1C9CF964'; -- Haris Imran
DECLARE @HA  uniqueidentifier = 'FBADE052-C9C9-4075-91AE-DBC04F9082CD'; -- Hasan Anas

IF (SELECT COUNT(DISTINCT OverNumber) FROM Deliveries WHERE InningsId = @inn AND BowlerId = @HA) <> 2
BEGIN
    ROLLBACK;
    THROW 50001, 'Hasan Anas is not already on 2 overs. Abort.', 1;
END;

IF (SELECT COUNT(*) FROM Deliveries WHERE InningsId = @inn AND OverNumber = 9 AND BowlerId = @HI AND IsLegalDelivery = 1) <> 6
BEGIN
    ROLLBACK;
    THROW 50002, 'Over 9 is not Haris Imran with 6 legal balls. Abort.', 1;
END;

IF (SELECT SUM(RunsOffBat + ExtraRuns) FROM Deliveries WHERE InningsId = @inn AND OverNumber = 9 AND BowlerId = @HI) <> 21
BEGIN
    ROLLBACK;
    THROW 50003, 'Over 9 is not 21 runs. Abort.', 1;
END;

UPDATE Deliveries
SET BowlerId = @AR, Version = Version + 1
WHERE InningsId = @inn AND OverNumber = 9 AND BowlerId = @HI;

UPDATE Matches
SET Version = Version + 1, UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = @mat;

DECLARE @arOvers int, @arRuns int, @hiOvers int, @haOvers int, @total int;

SELECT @arOvers = COUNT(DISTINCT OverNumber),
       @arRuns  = SUM(RunsOffBat + ExtraRuns)
FROM Deliveries WHERE InningsId = @inn AND BowlerId = @AR;

SELECT @hiOvers = COUNT(DISTINCT OverNumber)
FROM Deliveries WHERE InningsId = @inn AND BowlerId = @HI;

SELECT @haOvers = COUNT(DISTINCT OverNumber)
FROM Deliveries WHERE InningsId = @inn AND BowlerId = @HA;

SELECT @total = SUM(RunsOffBat + ExtraRuns)
FROM Deliveries WHERE InningsId = @inn;

IF @arOvers <> 2 OR @haOvers <> 2 OR @hiOvers <> 1 OR @arRuns <> 24 OR @total <> 103
BEGIN
    ROLLBACK;
    THROW 50004, 'Verification failed after bowling reassign. Rolled back.', 1;
END;

COMMIT;
PRINT 'OK: Hasan Anas 2.0, Amaan Rehan 2.0 (24 runs), Haris Imran 1.0, total 103.';
