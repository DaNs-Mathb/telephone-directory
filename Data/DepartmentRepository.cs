using Npgsql;
public class DepartmentRepository : IDepartmentRepository
{
    private readonly string _connectionString;

    public DepartmentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    public async Task<IEnumerable<Department>> GetHierarchyAsync()
    {
        try
        {
            var allDepts = await GetAllAsync();
            var lookup = allDepts.ToLookup(d => d.ParentDepartmentId);
            return BuildTree(null);

            IEnumerable<Department> BuildTree(int? parentId)
            {
                foreach (var dept in lookup[parentId])
                {
                    yield return new Department
                    {
                        ID = dept.ID,
                        Name = dept.Name,
                        ParentDepartmentId = dept.ParentDepartmentId,
                        Children = BuildTree(dept.ID).ToList()
                    };
                }
            }
        }
        catch (Exception ex)
        {

            throw new ApplicationException("Failed to build department hierarchy", ex);
        }
    }


    public async Task<IEnumerable<Department>> GetAllAsync()
    {
        try
        {
            var departments = new List<Department>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("SELECT id, name, parent_department_id FROM departments", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                departments.Add(new Department
                {
                    ID = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ParentDepartmentId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
                });
            }
            return departments;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // Таблица не найдена
        {
            throw new InvalidOperationException("Departments table not found", ex);
        }

    }

    public async Task<Department?> GetByIdAsync(int id)
    {
        try
        {

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, parent_department_id FROM departments WHERE id = @id",
                conn);

            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Department
                {
                    ID = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ParentDepartmentId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
                };
            }
            return null;
        }
        catch
        {
            throw;
        }


    }

    public async Task<int> CreateAsync(Department department)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO departments (name, parent_department_id) VALUES (@name, @parentId) RETURNING id",
                conn, transaction);

            cmd.Parameters.AddWithValue("@name", department.Name);
            cmd.Parameters.AddWithValue("@parentId", department.ParentDepartmentId ?? (object)DBNull.Value);

            var id = (int)(await cmd.ExecuteScalarAsync() ?? throw new Exception("Insert failed"));
            await transaction.CommitAsync();
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Нарушение уникальности
        {
            await transaction.RollbackAsync();

            throw new InvalidOperationException("Department with this name already exists", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Нарушение FK
        {
            await transaction.RollbackAsync();

            throw new InvalidOperationException("Specified parent department does not exist", ex);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }



    public async Task<bool> UpdateAsync(Department department)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            if (department.ParentDepartmentId != null)
            {
                var allChildren = await GetChildIdsRecursive(department.ID);
                if (allChildren.Contains(department.ParentDepartmentId.Value))
                    throw new InvalidOperationException("Cyclic dependency detected");
            }

            await using var cmd = new NpgsqlCommand(
                "UPDATE departments SET name = @name, parent_department_id = @parentId WHERE id = @id",
                conn, transaction);

            cmd.Parameters.AddWithValue("@id", department.ID);
            cmd.Parameters.AddWithValue("@name", department.Name);
            cmd.Parameters.AddWithValue("@parentId", department.ParentDepartmentId ?? (object)DBNull.Value);

            int affected = await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return affected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();

            throw new InvalidOperationException("Department name already exists", ex);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    //для проверки иерархии
    private async Task<List<int>> GetChildIdsRecursive(int deptId)
    {
        var children = new List<int>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
        WITH RECURSIVE child_tree AS (
            SELECT id FROM departments WHERE id = @id
            UNION ALL
            SELECT d.id FROM departments d
            JOIN child_tree ct ON d.parent_department_id = ct.id
        )
        SELECT id FROM child_tree WHERE id != @id
        """,
            conn);

        cmd.Parameters.AddWithValue("@id", deptId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            children.Add(reader.GetInt32(0));
        }

        return children;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Сначала проверяем наличие связанных сотрудников
            await using var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM employees WHERE department_id = @id",
                conn, transaction);
            checkCmd.Parameters.AddWithValue("@id", id);

            var employeeCount = (long)(await checkCmd.ExecuteScalarAsync() ?? 0);
            if (employeeCount > 0)
                throw new InvalidOperationException("Cannot delete department with employees");

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM departments WHERE id = @id",
                conn, transaction);

            cmd.Parameters.AddWithValue("@id", id);

            int affected = await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return affected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Cannot delete department with existing references", ex);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

public class EmployeeRepository : IEmployeeRepository
{
    private readonly string _connectionString;

    public EmployeeRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Employee>> GetAllAsync()
    {
        var employees = new List<Employee>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT id, name, phone, position, department_id FROM employees", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            employees.Add(new Employee
            {
                ID = reader.GetInt32(0),
                Name = reader.GetString(1),
                Phone = reader.GetString(2),
                Position = reader.GetString(3),
                DepartmentId = reader.GetInt32(4)
            });
        }

        return employees;
    }

    public async Task<Employee?> GetByIdAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, phone, position, department_id FROM employees WHERE id = @id",
            conn);

        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new Employee
            {
                ID = reader.GetInt32(0),
                Name = reader.GetString(1),
                Phone = reader.GetString(2),
                Position = reader.GetString(3),
                DepartmentId = reader.GetInt32(4)
            };
        }

        return null;
    }

    public async Task<int> CreateAsync(Employee employee)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO employees (name, phone, position, department_id) VALUES (@name, @phone, @position, @departmentId) RETURNING id",
                conn, transaction);

            cmd.Parameters.AddWithValue("@name", employee.Name);
            cmd.Parameters.AddWithValue("@phone", employee.Phone);
            cmd.Parameters.AddWithValue("@position", employee.Position);
            cmd.Parameters.AddWithValue("@departmentId", employee.DepartmentId);

            var id = (int)(await cmd.ExecuteScalarAsync() ?? throw new Exception("Insert failed"));
            await transaction.CommitAsync();
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Specified department does not exist", ex);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<Employee>> SearchAsync(string query)
    {
        var employees = new List<Employee>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, phone, position, department_id FROM employees " +
            "WHERE LOWER(name) LIKE LOWER(@query) OR " +
            "phone LIKE @query OR " +
            "LOWER(position) LIKE LOWER(@query)",
            conn);

        cmd.Parameters.AddWithValue("@query", $"%{query}%");

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            employees.Add(new Employee
            {
                ID = reader.GetInt32(0),
                Name = reader.GetString(1),
                Phone = reader.GetString(2),
                Position = reader.GetString(3),
                DepartmentId = reader.GetInt32(4)
            });
        }

        return employees;
    }
    public async Task<bool> UpdateAsync(Employee employee)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE employees SET name = @name, phone = @phone, " +
            "position = @position, department_id = @departmentId WHERE id = @id",
            conn);

        cmd.Parameters.AddWithValue("@id", employee.ID);
        cmd.Parameters.AddWithValue("@name", employee.Name);
        cmd.Parameters.AddWithValue("@phone", employee.Phone);
        cmd.Parameters.AddWithValue("@position", employee.Position);
        cmd.Parameters.AddWithValue("@departmentId", employee.DepartmentId);

        int affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM employees WHERE id = @id",
                conn);

            cmd.Parameters.AddWithValue("@id", id);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch
        {
            throw;
        }
    }
}