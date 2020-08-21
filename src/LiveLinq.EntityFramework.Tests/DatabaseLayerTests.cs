using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoMapper;
using ComposableCollections;
using ComposableCollections.Dictionary;
using FluentAssertions;
using LiveLinq.Dictionary;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleMonads;
using UtilityDisposables;

namespace LiveLinq.EntityFramework.Tests
{
    public class WorkItemDto
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public PersonDto AssignedTo { get; set; }
        public Guid? AssignedToForeignKey { get; set; }
    }

    public class PersonDto
    {
        public PersonDto()
        {
        }

        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<WorkItemDto> AssignedWorkItems { get; set; }
    }
    
    public class WorkItem {
        public WorkItem(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public string Description { get; set; }
        public Person AssignedTo { get; set; }
    }

    public class Person
    {
        public Person(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public string Name { get; set; }
        public ICollection<WorkItem> AssignedWorkItems { get; set; }
    }

    public class MyDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PersonDto>()
                .HasMany(x => x.AssignedWorkItems)
                .WithOne(x => x.AssignedTo)
                .HasForeignKey(x => x.AssignedToForeignKey);

            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=tasks.db");
        }
        
        public DbSet<WorkItemDto> WorkItem { get; set; }
        public DbSet<PersonDto> Person { get; set; }
    }

    public static class Transaction
    {
        public static Transaction<TPeople, TTasks> Create<TPeople, TTasks>(TPeople people, TTasks tasks, IDisposable disposable)
        {
            return new Transaction<TPeople, TTasks>(people, tasks, disposable);
        }
    }
    
    public class Transaction<TPeople, TTasks> : IDisposable
    {
        private readonly IDisposable _disposable;

        public Transaction(TPeople people, TTasks tasks, IDisposable disposable)
        {
            _disposable = disposable;
            People = people;
            Tasks = tasks;
        }

        public TPeople People { get; }
        public TTasks Tasks { get; }

        public void Dispose()
        {
            _disposable.Dispose();
        }
    }

    [TestClass]
    public class DatabaseLayerTests
    {
        [TestMethod]
        public void ShouldHandleManyToOneRelationships()
        {
            if (File.Exists("tasks.db"))
            {
                File.Delete("tasks.db");
            }
            
            var preserveReferencesState = new PreserveReferencesState();
            
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<WorkItem, WorkItemDto>()
                    .ConstructUsing(preserveReferencesState)
                    .ReverseMap()
                    .ConstructUsing(preserveReferencesState, dto => new WorkItem(dto.Id));

                cfg.CreateMap<Person, PersonDto>()
                    .ConstructUsing(preserveReferencesState)
                    .ReverseMap()
                    .ConstructUsing(preserveReferencesState, dto => new Person(dto.Id));
            });

            var mapper = mapperConfig.CreateMapper();
            
            var peopleChanges = new Subject<IDictionaryChangeStrict<Guid, Person>>();
            var taskChanges = new Subject<IDictionaryChangeStrict<Guid, WorkItem>>();

            var start = TransactionalDatabase.Create(() => new MyDbContext(), x => x.Database.Migrate());
            var infrastructure = start.Select(
                dbContext =>
                {
                    var tasks = dbContext.AsComposableReadOnlyDictionary(x => x.WorkItem, x => x.Id)
                        .WithMapping<Guid, WorkItem, WorkItemDto>(mapper)
                        .WithLiveLinq(taskChanges)
                        .WithBuiltInKey(t => t.Id);
                    var people = dbContext.AsComposableReadOnlyDictionary(x => x.Person, x => x.Id)
                        .WithMapping<Guid, Person, PersonDto>(mapper)
                        .WithLiveLinq(peopleChanges)
                        .WithBuiltInKey(p => p.Id);
                    return Transaction.Create(people, tasks, dbContext);
                },
                dbContext =>
                {
                    var tasks = dbContext.AsComposableDictionary(x => x.WorkItem, x => x.Id)
                        .WithMapping<Guid, WorkItem, WorkItemDto>(mapper)
                        .WithLiveLinq(taskChanges)
                        .WithBuiltInKey(t => t.Id);
                    var people = dbContext.AsComposableDictionary(x => x.Person, x => x.Id)
                        .WithMapping<Guid, Person, PersonDto>(mapper)
                        .WithLiveLinq(peopleChanges)
                        .WithBuiltInKey(p => p.Id);
                    return Transaction.Create(people, tasks, new AnonymousDisposable(() =>
                    {
                        dbContext.SaveChanges();
                        dbContext.Dispose();
                    }));
                });

            var joeId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            
            using (var transaction = infrastructure.BeginWrite())
            {
                var joe = new Person(joeId)
                {
                    Name = "Joe"
                };

                transaction.People.Add(joe);
            
                var washTheCar = new WorkItem(taskId)
                {
                    Description = "Wash the car",
                    AssignedTo = joe
                };
            
                transaction.Tasks.Add(washTheCar);   
            }

            using (var transaction = infrastructure.BeginWrite())
            {
                var joe = transaction.People[joeId];
                var washTheCar = transaction.Tasks[taskId];
            }
        }
    }
}
