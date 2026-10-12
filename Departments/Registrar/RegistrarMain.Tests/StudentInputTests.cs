using System.Text.Json.Nodes;
using RegistrarMain.Services;

namespace RegistrarMain.Tests;

public sealed class StudentInputTests
{
    [Fact]
    public void Blank_id_is_allowed_and_generates_an_auto_id()
    {
        var error = StudentInput.Validate(null, "Jane Doe", "jane@example.edu", "BS Information Technology", "1st Year", null, null, out var input);
        Assert.Null(error);
        Assert.Equal("", input.StudentIdNumber);
        Assert.Equal("2026-00014", StudentInput.ResolveStudentId(input.StudentIdNumber, 14));
    }

    [Theory]
    [InlineData(null, "Name is required.")]
    [InlineData("   ", "Name is required.")]
    public void Blank_name_is_rejected(string? name, string expected) =>
        Assert.Equal(expected, StudentInput.Validate("2026-1", name, "student@example.edu", "BS Information Technology", "1st Year", null, null, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("student.example.edu")]
    public void Invalid_email_is_rejected(string? email) =>
        Assert.Equal("A valid email address is required.", StudentInput.Validate("2026-1", "Jane Doe", email, "BS Information Technology", "1st Year", null, null, out _));

    [Fact]
    public void Unknown_program_is_rejected() =>
        Assert.Equal("Please select a valid degree program.", StudentInput.Validate("2026-1", "Jane Doe", "student@example.edu", "Unknown", "1st Year", null, null, out _));

    [Fact]
    public void Unknown_year_level_is_rejected() =>
        Assert.Equal("Please select a valid year level.", StudentInput.Validate("2026-1", "Jane Doe", "student@example.edu", "BS Information Technology", "Year 9", null, null, out _));

    [Fact]
    public void Duplicate_id_and_email_are_matched_case_insensitively()
    {
        Assert.True(StudentInput.HasMatch(["2026-ABCDE"], "2026-abcde"));
        Assert.True(StudentInput.HasMatch(["Student@Example.edu"], "student@example.edu"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void Null_blank_or_unknown_lookup_returns_the_existing_not_found_message(string? studentIdNumber)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StudentInput.FindUserByStudentId([new JsonObject { ["student_id_number"] = "2026-00001" }], studentIdNumber));

        Assert.Equal("Student account was not found.", error.Message);
    }
}
