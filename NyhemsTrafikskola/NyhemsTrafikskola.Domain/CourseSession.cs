namespace NyhemsTrafikskola.Domain;

public class CourseSession
{
    public const int StandardCapacity = 16;

    // Students may cancel online until this long before the course starts; after that only by phone.
    public static readonly TimeSpan OnlineCancellationNotice = TimeSpan.FromDays(2);

    public CourseSession(Guid courseId, DateTimeOffset startTime, DateTimeOffset endTime, int capacity, bool isPublished = false)
    {
        CourseId = courseId;
        StartTime = ValidateStartTime(startTime);
        EndTime = ValidateEndTime(startTime, endTime);
        Capacity = ValidateCapacity(capacity);
        IsPublished = isPublished;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CourseId { get; private set; }
    public DateTimeOffset StartTime { get; private set; }
    public DateTimeOffset EndTime { get; private set; }
    public int Capacity { get; private set; }
    public bool IsPublished { get; private set; }
    public bool IsCancelled { get; private set; }
    public bool IsArchived { get; private set; }

    public DateTimeOffset GetOnlineCancellationDeadline() => StartTime - OnlineCancellationNotice;

    public bool AllowsOnlineCancellation(DateTimeOffset utcNow) => !IsCancelled && utcNow < GetOnlineCancellationDeadline();

    public void Publish()
    {
        if (IsCancelled || IsArchived)
        {
            throw new InvalidOperationException("A cancelled or archived course session cannot be published.");
        }

        IsPublished = true;
    }

    public void Unpublish()
    {
        IsPublished = false;
    }

    public void Cancel()
    {
        IsPublished = false;
        IsCancelled = true;
    }

    public void Archive()
    {
        IsPublished = false;
        IsArchived = true;
    }

    public void UpdateSchedule(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        StartTime = ValidateStartTime(startTime);
        EndTime = ValidateEndTime(startTime, endTime);
    }

    public void UpdateCapacity(int capacity)
    {
        Capacity = ValidateCapacity(capacity);
    }

    private static DateTimeOffset ValidateStartTime(DateTimeOffset startTime)
    {
        return startTime;
    }

    private static DateTimeOffset ValidateEndTime(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        if (endTime <= startTime)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endTime));
        }

        return endTime;
    }

    private static int ValidateCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        return capacity;
    }
}
