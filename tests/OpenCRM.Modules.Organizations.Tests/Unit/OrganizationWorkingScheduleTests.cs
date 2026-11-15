using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Setup;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class OrganizationWorkingScheduleTests
{
    [Theory]
    [InlineData("null_breaks")]
    [InlineData("null_break")]
    [InlineData("closed_end")]
    [InlineData("closed_break")]
    public void ValidateRejectsMalformedBreaksAndClosedDayHours(string state)
    {
        var schedule = OrganizationWorkingScheduleTests.CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        switch (state)
        {
            case "null_breaks":
                schedule[1].Breaks = null!;
                break;
            case "null_break":
                schedule[1].Breaks = [null!];
                break;
            case "closed_end":
                schedule[0].EndTime = new TimeOnly(18, 0);
                break;
            case "closed_break":
                schedule[0].Breaks.Add(new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) });
                break;
        }

        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(schedule));
    }

    [Fact]
    public void CreateLegacySetupRejectsNegativeDay()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(9, 0), WorkdayEnd = new TimeOnly(18, 0), WorkingDays = [-1]
        }));
    }

    [Fact]
    public void CreateLegacyOrganizationProducesSevenDaysAndUsesLegacyHours()
    {
        var organization = new Organization
        {
            WorkingDays = [1, 3, 5],
            WorkdayStart = new TimeOnly(9, 0),
            WorkdayEnd = new TimeOnly(18, 0)
        };

        var schedule = OrganizationWorkingSchedule.CreateLegacy(organization);

        Assert.Equal(7, schedule.Count);
        Assert.Equal(new[] { 1, 3, 5 }, schedule.Where(day => day.IsWorking).Select(day => day.DayOfWeek));
        Assert.All(schedule.Where(day => day.IsWorking), day =>
        {
            Assert.Equal(new TimeOnly(9, 0), day.StartTime);
            Assert.Equal(new TimeOnly(18, 0), day.EndTime);
        });
        Assert.All(schedule.Where(day => !day.IsWorking), day =>
        {
            Assert.Null(day.StartTime);
            Assert.Null(day.EndTime);
        });
    }

    [Fact]
    public void CreateLegacySetupUsesUniqueSelectedDays()
    {
        var schedule = OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(8, 30),
            WorkdayEnd = new TimeOnly(17, 0),
            WorkingDays = [1, 1, 4]
        });

        Assert.Equal(new[] { 1, 4 }, schedule.Where(day => day.IsWorking).Select(day => day.DayOfWeek));
        Assert.All(schedule.Where(day => day.IsWorking), day =>
        {
            Assert.Equal(new TimeOnly(8, 30), day.StartTime);
            Assert.Equal(new TimeOnly(17, 0), day.EndTime);
        });
    }

    [Fact]
    public void CreateLegacySetupRejectsMissingStartTime()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(
            new SaveOrganizationSetupDto { WorkdayEnd = new TimeOnly(18, 0), WorkingDays = [1] }));
    }

    [Fact]
    public void CreateLegacySetupRejectsMissingEndTime()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(
            new SaveOrganizationSetupDto { WorkdayStart = new TimeOnly(9, 0), WorkingDays = [1] }));
    }

    [Theory]
    [InlineData(9, 0, 9, 0)]
    [InlineData(18, 0, 9, 0)]
    public void CreateLegacySetupRequiresEndAfterStart(int startHour, int startMinute, int endHour, int endMinute)
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(startHour, startMinute),
            WorkdayEnd = new TimeOnly(endHour, endMinute),
            WorkingDays = [1]
        }));
    }

    [Fact]
    public void CreateLegacySetupRejectsEmptyWorkingDays()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(9, 0),
            WorkdayEnd = new TimeOnly(18, 0),
            WorkingDays = []
        }));
    }

    [Fact]
    public void CreateLegacySetupRejectsNullWorkingDays()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(9, 0),
            WorkdayEnd = new TimeOnly(18, 0),
            WorkingDays = null!
        }));
    }

    [Fact]
    public void CreateLegacySetupRejectsOutOfRangeDay()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.CreateLegacy(new SaveOrganizationSetupDto
        {
            WorkdayStart = new TimeOnly(9, 0),
            WorkdayEnd = new TimeOnly(18, 0),
            WorkingDays = [7]
        }));
    }

    [Fact]
    public void CreateFromEntitiesReturnsSevenDaysAndSortsBreaks()
    {
        var schedule = OrganizationWorkingSchedule.Create(
        [
            new OrganizationWorkingDay
            {
                DayOfWeek = 2,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(18, 0),
                Breaks =
                [
                    new OrganizationWorkingDayBreak { StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(14, 0) },
                    new OrganizationWorkingDayBreak { StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 30) }
                ]
            }
        ]);

        Assert.Equal(7, schedule.Count);
        Assert.False(schedule[0].IsWorking);
        Assert.True(schedule[2].IsWorking);
        Assert.Equal(new TimeOnly(8, 0), schedule[2].StartTime);
        Assert.Collection(schedule[2].Breaks,
            value => Assert.Equal(new TimeOnly(11, 0), value.StartTime),
            value => Assert.Equal(new TimeOnly(13, 0), value.StartTime));
    }

    [Fact]
    public void HasDetailedDetectsBreaksAndDifferentHours()
    {
        var dayWithBreak = new OrganizationWorkingDay
        {
            Breaks = [new OrganizationWorkingDayBreak()]
        };
        var dayWithDifferentHours = new OrganizationWorkingDay { StartTime = new TimeOnly(10, 0) };

        Assert.True(OrganizationWorkingSchedule.HasDetailed([dayWithBreak]));
        Assert.True(OrganizationWorkingSchedule.HasDetailed([new OrganizationWorkingDay(), dayWithDifferentHours]));
        Assert.False(OrganizationWorkingSchedule.HasDetailed([new OrganizationWorkingDay(), new OrganizationWorkingDay()]));
        Assert.False(OrganizationWorkingSchedule.HasDetailed([]));
    }

    [Fact]
    public void ValidateRequiresSevenUniqueDaysInRange()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(null!));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(CreateSchedule().Take(6).ToList()));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(CreateSchedule(nullDay: true)));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(CreateSchedule(duplicateDay: true)));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(CreateSchedule(invalidDay: true)));
    }

    [Fact]
    public void ValidateRequiresAtLeastOneWorkingDay()
    {
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(CreateSchedule()));
    }

    [Fact]
    public void ValidateRejectsNullBreakCollectionsAndItems()
    {
        var nullCollection = CreateSchedule();
        nullCollection[0].IsWorking = true;
        nullCollection[0].StartTime = new TimeOnly(9, 0);
        nullCollection[0].EndTime = new TimeOnly(18, 0);
        nullCollection[0].Breaks = null!;
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(nullCollection));

        var nullItem = CreateSchedule();
        nullItem[0].IsWorking = true;
        nullItem[0].StartTime = new TimeOnly(9, 0);
        nullItem[0].EndTime = new TimeOnly(18, 0);
        nullItem[0].Breaks.Add(null!);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(nullItem));
    }

    [Fact]
    public void ValidateRejectsHoursAndBreaksOnClosedDay()
    {
        var withStart = CreateSchedule();
        withStart[0].StartTime = new TimeOnly(9, 0);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(withStart));

        var withEnd = CreateSchedule();
        withEnd[0].EndTime = new TimeOnly(18, 0);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(withEnd));

        var withBreak = CreateSchedule();
        withBreak[0].Breaks.Add(new OrganizationWorkingDayBreakDto());
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(withBreak));
    }

    [Fact]
    public void ValidateRequiresStartAndEndTimeForOpenDay()
    {
        var missingStart = CreateSchedule();
        missingStart[1].IsWorking = true;
        missingStart[1].EndTime = new TimeOnly(18, 0);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(missingStart));

        var missingEnd = CreateSchedule();
        missingEnd[1].IsWorking = true;
        missingEnd[1].StartTime = new TimeOnly(9, 0);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(missingEnd));
    }

    [Fact]
    public void ValidateRequiresEndAfterStart()
    {
        var schedule = CreateWorkingSchedule(new TimeOnly(18, 0), new TimeOnly(9, 0));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(schedule));
        schedule = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(9, 0));
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(schedule));
    }

    [Fact]
    public void ValidateRejectsBreakBeforeWorkday()
    {
        var schedule = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        schedule[1].Breaks.Add(new OrganizationWorkingDayBreakDto
        {
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(9, 30)
        });

        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(schedule));
    }

    [Fact]
    public void ValidateRejectsBreakAfterWorkday()
    {
        var schedule = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        schedule[1].Breaks.Add(new OrganizationWorkingDayBreakDto
        {
            StartTime = new TimeOnly(17, 30),
            EndTime = new TimeOnly(18, 30)
        });

        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(schedule));
    }

    [Fact]
    public void ValidateRejectsEmptyAndOverlappingBreaks()
    {
        var empty = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        empty[1].Breaks.Add(new OrganizationWorkingDayBreakDto
        {
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(12, 0)
        });
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(empty));

        var overlapping = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        overlapping[1].Breaks.AddRange(
        [
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) },
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 30), EndTime = new TimeOnly(13, 30) }
        ]);
        Assert.Throws<InvalidOperationException>(() => OrganizationWorkingSchedule.Validate(overlapping));
    }

    [Fact]
    public void ValidateSortsBreaksAndAllowsAdjacentIntervals()
    {
        var schedule = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        schedule[1].Breaks.AddRange(
        [
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(14, 0) },
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) }
        ]);

        var days = OrganizationWorkingSchedule.Validate(schedule);

        Assert.Equal(1, Assert.Single(days).DayOfWeek);
        Assert.Equal(new TimeOnly(12, 0), schedule[1].Breaks[0].StartTime);
    }

    [Fact]
    public void ValidateAllowsClosedDaysWithoutHoursOrBreaksAlongsideAnOpenDay()
    {
        var schedule = CreateWorkingSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));

        var days = OrganizationWorkingSchedule.Validate(schedule);

        Assert.Equal(1, Assert.Single(days).DayOfWeek);
    }

    private static List<OrganizationWorkingDayDto> CreateSchedule(
        bool nullDay = false,
        bool duplicateDay = false,
        bool invalidDay = false)
    {
        var schedule = Enumerable.Range(0, 7)
            .Select(day => new OrganizationWorkingDayDto { DayOfWeek = day })
            .ToList();
        if (nullDay)
        {
            schedule[0] = null!;
        }

        if (duplicateDay)
        {
            schedule[6].DayOfWeek = 5;
        }

        if (invalidDay)
        {
            schedule[6].DayOfWeek = 7;
        }

        return schedule;
    }

    private static List<OrganizationWorkingDayDto> CreateWorkingSchedule(TimeOnly start, TimeOnly end)
    {
        var schedule = CreateSchedule();
        schedule[1].IsWorking = true;
        schedule[1].StartTime = start;
        schedule[1].EndTime = end;

        return schedule;
    }
}
