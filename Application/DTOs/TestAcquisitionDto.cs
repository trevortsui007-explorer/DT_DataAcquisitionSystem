using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class TestAcquisitionStartRequestDto
    {
        public int ConfigId { get; set; }
        public string TestFileMode { get; set; } = "copy";
        public string TestFileLocation { get; set; }
        public string LocalTestDirectory { get; set; }
        public bool RunPostProcessing { get; set; } = true;
    }

    public class TestAcquisitionSelectSourceRequestDto
    {
        public string FullPath { get; set; }
    }

    public class TestAcquisitionRunDto
    {
        public string TestRunId { get; set; }
        public int ConfigId { get; set; }
        public string EqName { get; set; }
        public string TableName { get; set; }
        public string SourceFilePath { get; set; }
        public string TestFilePath { get; set; }
        public string TestFileName { get; set; }
        public string TestFileMode { get; set; }
        public string TestFileLocation { get; set; }
        public string LocalTestDirectory { get; set; }
        public bool RunPostProcessing { get; set; } = true;
        public bool DeleteTestFileOnCleanup { get; set; }
        public string CleanupKeyMode { get; set; }
        public List<string> InsertedIds { get; set; } = new List<string>();
        public bool ManualSourceSelectionRequired { get; set; }
        public List<FileEntryDto> SourceCandidates { get; set; } = new List<FileEntryDto>();
        public string SourceCandidateError { get; set; }
        public string TaskLogId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public int ProcessedRows { get; set; }
        public bool CanCleanup { get; set; }
        public bool Cleaned { get; set; }
        public string CleanupMessage { get; set; }
        public List<TestAcquisitionStepDto> Steps { get; set; } = new List<TestAcquisitionStepDto>();
        public List<TaskDetailLogDto> Details { get; set; } = new List<TaskDetailLogDto>();
        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
    }

    public class TestAcquisitionStepDto
    {
        public int Order { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }

    public class TestAcquisitionCleanupResultDto
    {
        public string TestRunId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public int DeletedTargetRows { get; set; }
        public int DeletedLogRows { get; set; }
        public int DeletedTaskLogRows { get; set; }
        public int DeletedFileStateRows { get; set; }
        public bool DeletedTestFile { get; set; }
        public string CleanupKeyMode { get; set; }
    }
}
