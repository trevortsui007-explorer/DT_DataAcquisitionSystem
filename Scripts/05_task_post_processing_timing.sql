IF COL_LENGTH('dbo.DA_AcquisitionTask', 'PostProcessingTiming') IS NULL
BEGIN
    ALTER TABLE dbo.DA_AcquisitionTask
    ADD PostProcessingTiming TINYINT NOT NULL
        CONSTRAINT DF_DA_AcquisitionTask_PostProcessingTiming DEFAULT (0);
END
GO