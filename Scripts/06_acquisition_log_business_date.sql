IF COL_LENGTH('dbo.DA_AcquisitionLog', 'BusinessDate') IS NULL
BEGIN
    ALTER TABLE dbo.DA_AcquisitionLog
    ADD BusinessDate date NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_DA_AcquisitionLog_NextRow'
      AND object_id = OBJECT_ID('dbo.DA_AcquisitionLog')
)
BEGIN
    CREATE INDEX IX_DA_AcquisitionLog_NextRow
    ON dbo.DA_AcquisitionLog
    (
        ConfigId,
        BusinessDate,
        FileName,
        Status,
        EndTime DESC
    )
    INCLUDE (StartRow, ProcessedRows);
END
GO
