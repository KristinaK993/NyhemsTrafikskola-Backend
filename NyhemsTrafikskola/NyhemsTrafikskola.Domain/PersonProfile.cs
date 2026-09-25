namespace NyhemsTrafikskola.Domain;

public abstract class PersonProfile
{
    protected PersonProfile(string firstName, string lastName, string email)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string FirstName { get; }
    public string LastName { get; }
    public string Email { get; }
    public string? UserId { get; set; }
}
