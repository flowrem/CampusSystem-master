namespace RegistrarMain.Contracts;

public record CourseDto(int Id, string Code, string Title, int Credits);
public record CreateCourseRequest(string Code, string Title, int Credits);
public record EnrollmentRequestDto(int CourseId, string Semester);
public record EnrollmentDto(int Id, int CourseId, string CourseCode, string Semester, string RowVersion);
public record TranscriptEntryDto(string CourseCode, string Grade);
public record TranscriptSemesterDto(string Semester, IReadOnlyList<TranscriptEntryDto> Entries);
public record TranscriptDto(IReadOnlyList<TranscriptSemesterDto> Semesters);
public record VerificationRequestDto(string? Purpose = null);
public record VerificationResponseDto(int Id, string Status, DateTime RequestedAt);
public record RecordsRequestDto(string DocumentType);
public record RecordsResponseDto(int Id, string DocumentType, string Status, DateTime RequestedAt);
