-- Innings 2 bowling: last over (over 9) was bowled by A Rahman Asim, not Ali Ahmed.
-- After fix: Ali Ahmed 2 overs (0, 6), A Rahman Asim 2 overs (5, 9). Total stays 91.

SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @inn uniqueidentifier = 'E24EFB74-2DF4-47CA-B134-FFABA28ACDC9';
DECLARE @mat uniqueidentifier = '408534AB-B63F-431B-A6E6-31D4B8A89356';
DECLARE @AA  uniqueidentifier = '170C48FC-9334-4C76-ABC7-69515AD5A68A'; -- Ali Ahmed
DECLARE @AS  uniqueidentifier = '3BBA14A0-15EA-4FD5-9307-46C57392DAFA'; -- A Rahman Asim

IF (SELECT COUNT(*) FROM Deliveries WHERE InningsId = @inn AND OverNumber = 9 AND BowlerId = @AA AND IsLegalDelivery = 1) <> 6
BEGIN
    ROLLBACK;
    THROW 50001, 'Over 9 is not Ali Ahmed with 6 legal balls. Abort.', 1;
END;

UPDATE Deliveries
SET BowlerId = @AS, Version = Version + 1
WHERE InningsId = @inn AND OverNumber = 9 AND BowlerId = @AA;

UPDATE Matches
SET Version = Version + 1, UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = @mat;

DECLARE @aaOvers int, @asOvers int, @total int;
SELECT @aaOvers = COUNT(DISTINCT OverNumber) FROM Deliveries WHERE InningsId = @inn AND BowlerId = @AA;
SELECT @asOvers = COUNT(DISTINCT OverNumber) FROM Deliveries WHERE InningsId = @inn AND BowlerId = @AS;
SELECT @total = SUM(RunsOffBat + ExtraRuns) FROM Deliveries WHERE InningsId = @inn;

IF @aaOvers <> 2 OR @asOvers <> 2 OR @total <> 91
BEGIN
    ROLLBACK;
    THROW 50002, 'Verification failed. Rolled back.', 1;
END;

COMMIT;
PRINT 'OK: Ali Ahmed 2.0, A Rahman Asim 2.0 (last over = Asim), total 91.';
