/*
    层压板厚重复数据清理脚本
    场景：文件夹补采按 2026-07-01 到 2026-07-19 执行时，同一月份目录被重复扫描。
    重复键：FullFilePath + SourceRow
    保留规则：保留最早 CreatedDt；CreatedDt 相同则保留最小 Id。

    使用方式：
    1. 先执行“预览重复组”和“预览将删除的数据”。
    2. 确认数量无误后，再取消“删除重复数据”段的注释执行。
*/

DECLARE @StartCreatedDt datetime = '2026-07-20 11:00:00';
DECLARE @EndCreatedDt datetime = '2026-07-20 12:00:00';

-- 1. 预览重复组
SELECT
    FullFilePath,
    SourceRow,
    COUNT(1) AS DuplicateCount,
    MIN(CreatedDt) AS FirstCreatedDt,
    MAX(CreatedDt) AS LastCreatedDt
FROM dbo.DA_LaminationBoardThicknessDaily
WHERE CreatedDt >= @StartCreatedDt
  AND CreatedDt < @EndCreatedDt
  AND ISNULL(FullFilePath, '') <> ''
  AND SourceRow IS NOT NULL
GROUP BY FullFilePath, SourceRow
HAVING COUNT(1) > 1
ORDER BY DuplicateCount DESC, FullFilePath, SourceRow;

-- 2. 预览将删除的数据
;WITH Ranked AS
(
    SELECT
        *,
        ROW_NUMBER() OVER (
            PARTITION BY FullFilePath, SourceRow
            ORDER BY CreatedDt ASC, Id ASC
        ) AS rn
    FROM dbo.DA_LaminationBoardThicknessDaily
    WHERE CreatedDt >= @StartCreatedDt
      AND CreatedDt < @EndCreatedDt
      AND ISNULL(FullFilePath, '') <> ''
      AND SourceRow IS NOT NULL
)
SELECT *
FROM Ranked
WHERE rn > 1
ORDER BY FullFilePath, SourceRow, CreatedDt, Id;

/*
-- 3. 删除重复数据：确认上方预览无误后再执行
BEGIN TRAN;

;WITH Ranked AS
(
    SELECT
        Id,
        ROW_NUMBER() OVER (
            PARTITION BY FullFilePath, SourceRow
            ORDER BY CreatedDt ASC, Id ASC
        ) AS rn
    FROM dbo.DA_LaminationBoardThicknessDaily
    WHERE CreatedDt >= @StartCreatedDt
      AND CreatedDt < @EndCreatedDt
      AND ISNULL(FullFilePath, '') <> ''
      AND SourceRow IS NOT NULL
)
DELETE T
FROM dbo.DA_LaminationBoardThicknessDaily AS T
INNER JOIN Ranked AS R
    ON T.Id = R.Id
WHERE R.rn > 1;

SELECT @@ROWCOUNT AS DeletedRows;

-- 确认无误后执行 COMMIT；如有问题执行 ROLLBACK。
-- COMMIT;
-- ROLLBACK;
*/
