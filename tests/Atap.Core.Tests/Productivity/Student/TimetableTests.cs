using System.Text.Json;
using Atap.Core.Productivity.Student;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Student;

public class TimetableTests
{
    /// <summary>
    /// A semester timetable as saved: classes Monday to Friday, nothing at the weekend, one
    /// no-class Monday, a class that ends before it starts, a class with no code or name, a null
    /// entry and a field from a newer version.
    /// </summary>
    private const string SavedTimetable = """
        {
          "schemaVersion": 1,
          "firstDay": "2026-10-05",
          "lastDay": "2027-01-29",
          "noClassDays": ["2026-11-09", "2026-11-09", "0001-01-01"],
          "classes": [
            {
              "id": "csc584-lec",
              "code": "CSC584",
              "name": "Enterprise Programming",
              "room": "Bilik 2A",
              "lecturer": "Pn. Aminah",
              "day": "monday",
              "start": "10:00",
              "end": "12:00",
              "colour": "teal"
            },
            { "id": "tmc501", "code": "TMC501", "name": "Etika Profesional", "room": "DK 4", "day": "tuesday", "start": "08:00", "end": "10:00" },
            { "id": "csc584-lab", "code": "CSC584", "name": "Enterprise Programming", "room": "Makmal 3", "day": "wednesday", "start": "14:00", "end": "16:00" },
            { "id": "isp573", "code": "ISP573", "name": "Information Systems Project", "room": "Bilik 1B", "day": "thursday", "start": "11:00", "end": "13:00" },
            { "id": "ict650", "code": "ICT650", "name": "Mobile Development", "room": "Makmal 1", "day": "friday", "start": "08:30", "end": "10:30" },
            { "id": "terbalik", "code": "XYZ100", "day": "friday", "start": "15:00", "end": "14:00" },
            { "id": "tanpa-nama", "code": " ", "name": "", "day": "friday", "start": "16:00", "end": "17:00" },
            null
          ],
          "weekNumbers": true
        }
        """;

    private static TimetableDocument Load() =>
        JsonSerializer.Deserialize(SavedTimetable, StudentJsonContext.Default.TimetableDocument)!.Normalize();

    [Fact]
    public void DuringAClass_ShowsItAndTheNextOne()
    {
        var outlook = Load().OutlookAt(My(2026, 10, 26, 11, 0), MalaysiaZone);

        Assert.Equal("csc584-lec", outlook.Current!.Session.Id);
        Assert.Equal(My(2026, 10, 26, 12, 0), outlook.Current.End);
        Assert.Equal("tmc501", outlook.Next!.Session.Id);
        Assert.Equal(My(2026, 10, 27, 8, 0), outlook.Next.Start);
    }

    [Fact]
    public void FridayAfterTheLastClass_NextIsMonday()
    {
        var outlook = Load().OutlookAt(My(2026, 10, 30, 11, 0), MalaysiaZone);

        Assert.Null(outlook.Current);
        Assert.Equal("csc584-lec", outlook.Next!.Session.Id);
        Assert.Equal(new DateOnly(2026, 11, 2), outlook.Next.Date);
        Assert.Equal(My(2026, 11, 2, 10, 0), outlook.Next.Start);
    }

    [Fact]
    public void Saturday_NextIsMonday()
    {
        var outlook = Load().OutlookAt(My(2026, 10, 31, 9, 0), MalaysiaZone);

        Assert.Equal(My(2026, 11, 2, 10, 0), outlook.Next!.Start);
    }

    [Fact]
    public void SundayNight_NextIsMondayMorning()
    {
        var outlook = Load().OutlookAt(My(2026, 10, 25, 21, 0), MalaysiaZone);

        Assert.Equal(My(2026, 10, 26, 10, 0), outlook.Next!.Start);
    }

    [Fact]
    public void FridayAndSaturdayWeekend_NextClassAfterThursdayIsSunday()
    {
        // Johor, Kedah, Kelantan and Terengganu rest on Friday and Saturday, so classes run Sunday to Thursday.
        DayOfWeek[] schoolDays = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];
        var timetable = new TimetableDocument
        {
            Classes = [.. schoolDays.Select(day => new ClassSession
            {
                Id = day.ToString().ToLowerInvariant(),
                Code = "CSC584",
                Day = day,
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(10, 0),
            })],
        };

        var thursdayAfterClass = timetable.OutlookAt(My(2026, 10, 29, 11, 0), MalaysiaZone);
        var friday = timetable.OutlookAt(My(2026, 10, 30, 9, 0), MalaysiaZone);
        var saturdayNight = timetable.OutlookAt(My(2026, 10, 31, 21, 0), MalaysiaZone);

        Assert.Null(thursdayAfterClass.Current);
        Assert.Equal("sunday", thursdayAfterClass.Next!.Session.Id);
        Assert.Equal(new DateOnly(2026, 11, 1), thursdayAfterClass.Next.Date);
        Assert.Equal(My(2026, 11, 1, 8, 0), thursdayAfterClass.Next.Start);
        Assert.Null(friday.Current);
        Assert.Equal(My(2026, 11, 1, 8, 0), friday.Next!.Start);
        Assert.Equal(My(2026, 11, 1, 8, 0), saturdayNight.Next!.Start);
    }

    [Fact]
    public void OneClassAWeek_AfterItTheNextIsSevenDaysLater()
    {
        var timetable = new TimetableDocument { Classes = [Load().Find("csc584-lec")!] };

        var outlook = timetable.OutlookAt(My(2026, 10, 26, 13, 0), MalaysiaZone);

        Assert.Equal(My(2026, 11, 2, 10, 0), outlook.Next!.Start);
    }

    [Fact]
    public void NoClassDay_IsSkipped()
    {
        var outlook = Load().OutlookAt(My(2026, 11, 6, 11, 0), MalaysiaZone);

        Assert.Equal("tmc501", outlook.Next!.Session.Id);
        Assert.Equal(My(2026, 11, 10, 8, 0), outlook.Next.Start);
    }

    [Fact]
    public void BeforeTheSemester_NextIsTheFirstClassOfTheFirstDay()
    {
        var outlook = Load().OutlookAt(My(2026, 9, 20, 9, 0), MalaysiaZone);

        Assert.Equal(My(2026, 10, 5, 10, 0), outlook.Next!.Start);
    }

    [Fact]
    public void AfterTheLastDay_ThereIsNoNextClass()
    {
        var outlook = Load().OutlookAt(My(2027, 1, 29, 11, 0), MalaysiaZone);

        Assert.Equal(ClassOutlook.None, outlook);
    }

    [Fact]
    public void OverlappingClasses_TheOneThatStartedLastIsCurrent()
    {
        var first = new ClassSession { Id = "a", Code = "AAA100", Day = DayOfWeek.Monday, Start = new TimeOnly(10, 0), End = new TimeOnly(12, 0) };
        var second = first with { Id = "b", Code = "BBB100", Start = new TimeOnly(11, 0), End = new TimeOnly(13, 0) };

        var outlook = new TimetableDocument { Classes = [first, second] }.OutlookAt(My(2026, 10, 26, 11, 30), MalaysiaZone);

        Assert.Equal("b", outlook.Current!.Session.Id);
    }

    [Theory]
    [InlineData(9, 44, false)]
    [InlineData(9, 45, true)]
    [InlineData(10, 30, true)]
    [InlineData(12, 0, false)]
    public void IsLive_DuringAClassAndFrom15MinutesBefore(int hour, int minute, bool expected)
    {
        var now = My(2026, 10, 26, hour, minute);

        Assert.Equal(expected, Load().OutlookAt(now, MalaysiaZone).IsLiveAt(now));
    }

    [Theory]
    [InlineData(9, 0, 2026, 10, 26, 9, 45)]
    [InlineData(9, 45, 2026, 10, 26, 10, 0)]
    [InlineData(10, 30, 2026, 10, 26, 12, 0)]
    [InlineData(12, 0, 2026, 10, 27, 0, 0)]
    public void NextChange_IsTheNextLiveOrClassChangeOrMidnight(
        int hour, int minute, int year, int month, int day, int changeHour, int changeMinute)
    {
        var now = My(2026, 10, 26, hour, minute);

        var change = Load().OutlookAt(now, MalaysiaZone).NextChange(now, MalaysiaZone);

        Assert.Equal(My(year, month, day, changeHour, changeMinute), change);
    }

    [Fact]
    public void EmptyTimetable_IsNeverLiveAndChangesAtMidnight()
    {
        var now = My(2026, 10, 25, 21, 0);

        var outlook = new TimetableDocument().OutlookAt(now, MalaysiaZone);

        Assert.False(outlook.IsLiveAt(now));
        Assert.Equal(My(2026, 10, 26, 0, 0), outlook.NextChange(now, MalaysiaZone));
    }

    [Fact]
    public void EmptyOrNullLists_LoadAsEmpty()
    {
        var empty = JsonSerializer.Deserialize("{}", StudentJsonContext.Default.TimetableDocument)!;
        var nulls = JsonSerializer.Deserialize("""{ "classes": null, "noClassDays": null }""", StudentJsonContext.Default.TimetableDocument)!;

        Assert.Equal(TimetableDocument.CurrentSchemaVersion, empty.SchemaVersion);
        Assert.Empty(empty.Classes);
        Assert.Equal(ClassOutlook.None, nulls.OutlookAt(My(2026, 10, 26, 9, 0), MalaysiaZone));
        Assert.Empty(nulls.Normalize().Classes);
        Assert.Empty(nulls.Normalize().NoClassDays);
    }

    [Fact]
    public void On_ListsADayByStartTime()
    {
        var timetable = Load().Upsert(new ClassSession
        {
            Id = "awal",
            Code = "MAT100",
            Day = DayOfWeek.Monday,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(9, 0),
        });

        Assert.Equal(["awal", "csc584-lec"], timetable.On(DayOfWeek.Monday).Select(c => c.Id));
        Assert.Empty(timetable.On(DayOfWeek.Saturday));
    }

    [Fact]
    public void SavedFile_NormalizeDropsInvalidClassesAndBadDates()
    {
        var timetable = Load();

        Assert.Equal(5, timetable.Classes.Count);
        Assert.Null(timetable.Find("terbalik"));
        Assert.Null(timetable.Find("tanpa-nama"));
        Assert.Equal([new DateOnly(2026, 11, 9)], timetable.NoClassDays);
    }

    [Fact]
    public void SavedFile_RoundTripsDayNamesTimesAndUnknownFields()
    {
        var timetable = Load();

        var json = JsonSerializer.Serialize(timetable, StudentJsonContext.Default.TimetableDocument);
        var again = JsonSerializer.Deserialize(json, StudentJsonContext.Default.TimetableDocument)!;

        Assert.Contains("\"day\": \"monday\"", json);
        Assert.Contains("\"start\": \"10:00\"", json);
        Assert.Contains("\"weekNumbers\": true", json);
        Assert.Contains("\"colour\": \"teal\"", json);
        Assert.Equal(
            timetable.Classes.Select(c => (c.Id, c.Code, c.Room, c.Day, c.Start, c.End)),
            again.Classes.Select(c => (c.Id, c.Code, c.Room, c.Day, c.Start, c.End)));
        Assert.Equal(timetable.FirstDay, again.FirstDay);
    }
}
