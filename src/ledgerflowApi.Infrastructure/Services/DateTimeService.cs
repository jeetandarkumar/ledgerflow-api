using ledgerflowApi.Application.Common.Interfaces;

namespace ledgerflowApi.Infrastructure.Services;

public class DateTimeService : IDateTimeService
{
    public DateTime UtcNow => DateTime.UtcNow;
}
