namespace ProbuildBackend.Models.DTO
{
    public class UserSearchDto
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserType { get; set; }
        public string CompanyName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string ConstructionType { get; set; }
        public string Trade { get; set; }
        public string SupplierType { get; set; }
        public string ProductsOffered { get; set; }
        public string Country { get; set; }
        public string City { get; set; }

        public static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return string.Empty;

            var parts = email.Split('@');
            if (parts.Length != 2)
                return email; // Invalid email format

            var username = parts[0];
            var domain = parts[1];

            if (username.Length <= 3)
                return $"{new string('*', username.Length)}@{domain}";

            return $"{username.Substring(0, 3)}{new string('*', username.Length - 3)}@{domain}";
        }

        public static string MaskPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            if (phoneNumber.Length <= 4)
                return new string('*', phoneNumber.Length);

            return $"{new string('*', phoneNumber.Length - 4)}{phoneNumber.Substring(phoneNumber.Length - 4)}";
        }
    }
}
