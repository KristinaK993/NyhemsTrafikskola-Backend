namespace NyhemsTrafikskola.Domain;

public class Course
{
    public Course(CourseType type, string title, decimal price, bool isActive = true)
    {
        Type = type;
        Title = ValidateTitle(title);
        Price = ValidatePrice(price);
        IsActive = isActive;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public CourseType Type { get; private set; }
    public string Title { get; private set; }
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Course title cannot be empty.", nameof(title));
        }

        return title.Trim();
    }

    private static decimal ValidatePrice(decimal price)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Course price cannot be negative.");
        }

        return price;
    }
}
