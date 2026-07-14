IF COL_LENGTH('dbo.DA_AcquisitionConfig', 'ParserType') IS NULL
BEGIN
    ALTER TABLE dbo.DA_AcquisitionConfig ADD
        ParserType NVARCHAR(100) NOT NULL CONSTRAINT DF_DA_AcquisitionConfig_ParserType DEFAULT ('standard-table'),
        TemplateId INT NULL,
        ParserOptions NVARCHAR(MAX) NULL;
END
GO

IF OBJECT_ID('dbo.DA_ExcelImportTemplate', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DA_ExcelImportTemplate
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateCode NVARCHAR(100) NOT NULL,
        TemplateName NVARCHAR(200) NOT NULL,
        ParserType NVARCHAR(100) NOT NULL,
        TemplateVersion INT NOT NULL DEFAULT 1,
        DefinitionJson NVARCHAR(MAX) NOT NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        CreateTime DATETIME NOT NULL DEFAULT GETDATE(),
        UpdateTime DATETIME NOT NULL DEFAULT GETDATE()
    );

    CREATE UNIQUE INDEX UQ_DA_ExcelImportTemplate_Code_Version
    ON dbo.DA_ExcelImportTemplate(TemplateCode, TemplateVersion);
END
GO

IF OBJECT_ID('dbo.DA_LaminationBoardThicknessDaily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DA_LaminationBoardThicknessDaily
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BusinessDate DATE NULL,
        ShiftName NVARCHAR(50) NULL,
        ProductCategory NVARCHAR(50) NULL,
        Workshop NVARCHAR(50) NULL,
        ModelNo NVARCHAR(100) NULL,
        BatchNo NVARCHAR(100) NULL,
        Quantity INT NULL,
        IsSample BIT NULL,
        IsMassProduction BIT NULL,
        StandardThickness DECIMAL(18,6) NULL,
        UpperLimit DECIMAL(18,6) NULL,
        LowerLimit DECIMAL(18,6) NULL,
        Measure01 DECIMAL(18,6) NULL,
        Measure02 DECIMAL(18,6) NULL,
        Measure03 DECIMAL(18,6) NULL,
        Measure04 DECIMAL(18,6) NULL,
        Measure05 DECIMAL(18,6) NULL,
        Measure06 DECIMAL(18,6) NULL,
        Measure07 DECIMAL(18,6) NULL,
        Measure08 DECIMAL(18,6) NULL,
        Measure09 DECIMAL(18,6) NULL,
        RangeValue DECIMAL(18,6) NULL,
        AverageValue DECIMAL(18,6) NULL,
        Judgment NVARCHAR(20) NULL,
        Remark NVARCHAR(1000) NULL,
        SourceRow INT NULL,
        FileName NVARCHAR(510) NULL,
        FullFilePath NVARCHAR(2000) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM dbo.DA_ExcelImportTemplate
    WHERE TemplateCode = N'lamination-thickness-daily'
      AND TemplateVersion = 1
)
BEGIN
    INSERT INTO dbo.DA_ExcelImportTemplate
    (
        TemplateCode,
        TemplateName,
        ParserType,
        TemplateVersion,
        DefinitionJson,
        IsEnabled
    )
    VALUES
    (
        N'lamination-thickness-daily',
        N'层压工序板厚检验日报表',
        N'template-excel',
        1,
        N'{
  "identity": {
    "templateCode": "lamination-thickness-daily",
    "templateName": "层压工序板厚检验日报表",
    "titleCell": "B2",
    "titleContains": "层压工序板厚检验日报表"
  },
  "metadata": [
    { "field": "BusinessDate", "source": "B3", "parser": "dateFromChineseText" },
    { "field": "ShiftName", "source": "F3", "parser": "checkedOption", "options": ["日班", "夜班"] },
    { "field": "ProductCategory", "source": "K3", "parser": "checkedOption", "options": ["汽车", "医疗", "常规"] },
    { "field": "Workshop", "source": "S3", "parser": "checkedOption", "options": ["一车间", "二车间"] }
  ],
  "dataRegion": {
    "startRow": 6,
    "stopRules": [
      {
        "type": "keyword",
        "columns": ["B", "C", "D", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W"],
        "keywords": ["总计", "异常记录"]
      },
      {
        "type": "emptyRows",
        "count": 2,
        "columns": ["B", "C", "J", "K", "L", "M", "N", "O", "P", "Q", "R"]
      }
    ],
    "skipRules": [
      {
        "type": "allEmpty",
        "columns": ["J", "K", "L", "M", "N", "O", "P", "Q", "R"]
      }
    ],
    "carryForwardColumns": ["B", "C", "D", "E", "F", "G", "H", "I"]
  },
  "columns": [
    { "field": "ModelNo", "column": "B", "type": "string", "carryForward": true },
    { "field": "BatchNo", "column": "C", "type": "string", "carryForward": true },
    { "field": "Quantity", "column": "D", "type": "int", "carryForward": true },
    { "field": "IsSample", "column": "E", "type": "booleanCheck", "carryForward": true },
    { "field": "IsMassProduction", "column": "F", "type": "booleanCheck", "carryForward": true },
    { "field": "StandardThickness", "column": "G", "type": "decimal", "carryForward": true },
    { "field": "UpperLimit", "column": "H", "type": "decimal", "carryForward": true },
    { "field": "LowerLimit", "column": "I", "type": "decimal", "carryForward": true },
    { "field": "Measure01", "column": "J", "type": "decimal" },
    { "field": "Measure02", "column": "K", "type": "decimal" },
    { "field": "Measure03", "column": "L", "type": "decimal" },
    { "field": "Measure04", "column": "M", "type": "decimal" },
    { "field": "Measure05", "column": "N", "type": "decimal" },
    { "field": "Measure06", "column": "O", "type": "decimal" },
    { "field": "Measure07", "column": "P", "type": "decimal" },
    { "field": "Measure08", "column": "Q", "type": "decimal" },
    { "field": "Measure09", "column": "R", "type": "decimal" },
    { "field": "RangeValue", "column": "S", "type": "decimal" },
    { "field": "AverageValue", "column": "T", "type": "decimal" },
    { "field": "Judgment", "type": "judgmentFromColumns", "accColumn": "U", "rejColumn": "V" },
    { "field": "Remark", "column": "W", "type": "string" }
  ],
  "systemFields": ["SourceRow", "FileName", "FullFilePath", "CreatedAt"]
}',
        1
    );
END
GO
