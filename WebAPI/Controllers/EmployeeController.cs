using Domain.Entities;
using Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using WebAPI.Dto;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeController : ControllerBase
    {
        public IApplicationDbContext _dbContext { get; }
        public IApplicationReadDbConnection _readDbConnection { get; }
        public IApplicationWriteDbConnection _writeDbConnection { get; }

        public EmployeeController(
            IApplicationDbContext dbContext,
            IApplicationReadDbConnection readDbConnection,
            IApplicationWriteDbConnection writeDbConnection)
        {
            _dbContext = dbContext;
            _readDbConnection = readDbConnection;
            _writeDbConnection = writeDbConnection;
        }

        // Using Dapper only for this read request
        // Flat level - does not return nested objects
        [HttpGet]
        public async Task<IActionResult> GetAllEmployees()
        {
            var query = $"SELECT * FROM Employees";
            var employees = await _readDbConnection.QueryAsync<Employee>(query);

            return Ok(employees);
        }

        // Using Eager Loading by Entity Framework Core to get nested object
        [HttpGet("{id}")] // enpoint: api/employee/{id}
        public async Task<IActionResult> GetAllEmployeesById(int id)
        {
            var employees = await _dbContext.Employees
                .Include(a => a.Department)
                .Where(a => a.Id == id)
                .ToListAsync();
            
            return Ok(employees);
        }

        // using the Same Transaction and Dapper / Entity Framework Core
        [HttpPost]
        public async Task<IActionResult> CreateEmployeeWithDepartment(
            EmployeeDto employeeDto)
        {
            _dbContext.Connection.Open();

            using (var transaction = _dbContext.Connection.BeginTransaction())
            {
                try
                {
                    _dbContext.Database.UseTransaction(transaction as DbTransaction);
                    
                    // Check duplicate department name
                    bool DepartmentExists = await _dbContext.Departments
                        .AnyAsync(a => a.Name == employeeDto.Department.Name);
                    
                    if (DepartmentExists)
                    {
                        throw new Exception("Department already exists");
                    }

                    // Add Department
                    var addDepartmentQuery = $"INSERT INTO Departments(Name,Description) " +
                        $"VALUES('{employeeDto.Department.Name}'," +
                        $"'{employeeDto.Department.Description}');" +
                        $"SELECT CAST(SCOPE_IDENTITY() as int)";

                    var departmentId = await _writeDbConnection
                        .QuerySingleAsync<int>(
                            addDepartmentQuery,
                            transaction: transaction);
                    
                    // Check department id is not Zero.
                    if (departmentId == 0)
                    {
                        throw new Exception("Department id error");
                    }

                    // Add the employee
                    var employee = new Employee
                    {
                        DepartmentId = departmentId,
                        Name = employeeDto.Name,
                        Email = employeeDto.Email
                    };

                    await _dbContext.Employees.AddAsync(employee);
                    await _dbContext.SaveChangesAsync(default);

                    // Commit the transaction
                    transaction.Commit();

                    // Return the employeeId
                    return Ok(employee.Id);
                }
                catch (Exception)
                {
                    // Safety for transaction
                    transaction.Rollback();
                    throw;
                }
                finally
                {
                    _dbContext.Connection.Close();
                }
            }
        }
    }
}
