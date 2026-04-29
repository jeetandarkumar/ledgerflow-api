using CleanArch.Application.Common.Interfaces;

namespace CleanArch.Infrastructure.Services;

public class DateTimeService : IDateTimeService
{
    public DateTime UtcNow => DateTime.UtcNow;
}
