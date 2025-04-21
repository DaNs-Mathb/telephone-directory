public interface IDepartmentRepository
{
    Task<IEnumerable<Department>> GetAllAsync();
    Task<Department?> GetByIdAsync(int id);
    Task<IEnumerable<Department>> GetHierarchyAsync();
    Task<int> CreateAsync(Department department);
    Task<bool> UpdateAsync(Department department); 
    Task<bool> DeleteAsync(int id); 
}

public interface IEmployeeRepository
{
    Task<IEnumerable<Employee>> GetAllAsync();
    Task<Employee?> GetByIdAsync(int id);
    Task<int> CreateAsync(Employee employee);
    Task<IEnumerable<Employee>> SearchAsync(string query);
    Task<bool> UpdateAsync(Employee employee); 
    Task<bool> DeleteAsync(int id); 
}