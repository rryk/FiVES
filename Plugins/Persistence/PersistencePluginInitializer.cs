using System;
using FIVES;
using NHibernate.Cfg;
using NHibernate;
using NHibernate.Tool.hbm2ddl;
using System.Collections.Generic;
using System.Diagnostics;
using Iesi.Collections.Generic;
using System.Threading;
using System.Data;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using NLog;


namespace PersistencePlugin
{
    public class PersistencePlugin: IPluginInitializer
    {
        #region IPluginInitializer implementation

        /// <summary>
        /// Returns the name of the plugin.
        /// </summary>
        /// <returns>The name of the plugin.</returns>
        public string GetName()
        {
            return "Persistence";
        }

        /// <summary>
        /// Returns the list of names of the plugins that this plugin depends on.
        /// </summary>
        /// <returns>The list of names of the plugins that this plugin depends on.</returns>
        public List<string> GetDependencies()
        {
            return new List<string>();
        }

        /// <summary>
        /// Initializes the plugin. This method will be called by the plugin manager when all dependency plugins have
        /// been loaded.
        /// </summary>
        public void Initialize()
        {
            InitializeNHibernate ();
            World.Instance.AddedEntity += OnEntityAdded;
            World.Instance.RemovedEntity += OnEntityRemoved;
            ComponentRegistry.Instance.UpgradedComponent += OnComponentUpgraded;
            InitializePersistedCollections();
            ThreadPool.QueueUserWorkItem(_ => PersistChangedEntities());
        }

        /// <summary>
        /// Initializes NHibernate. Adds the mappings based on the assembly, initializes the session factory and opens
        /// a long-running global session
        /// </summary>
        private void InitializeNHibernate() {
            NHibernateConfiguration.Configure ();
            NHibernateConfiguration.AddAssembly (typeof(Entity).Assembly);
            new SchemaUpdate (NHibernateConfiguration).Execute(false, true);
            SessionFactory = NHibernateConfiguration.BuildSessionFactory ();
            GlobalSession = SessionFactory.OpenSession ();
        }

        /// <summary>
        /// Initializes the state of the system as stored in the database by retrieving the stored component registry and
        /// the stored entities
        /// </summary>
        private void InitializePersistedCollections() {
            RetrieveComponentRegistryFromDatabase ();
            RetrieveEntitiesFromDatabase ();
        }

        #endregion

        #region Event Handlers
        /// <summary>
        /// Event Handler for EntityAdded event fired by the EntityRegistry. A new entity will be persisted to the database,
        /// unless the entity to be added was just queried from the database (e.g. upon initialization)
        /// </summary>
        /// <param name="sender">Sender of the event (the EntityRegistry)</param>
        /// <param name="e">Event arguments</param>
        internal void OnEntityAdded(Object sender, EntityEventArgs e)
        {
            Entity addedEntity = e.Entity;
            // Only persist entities if they are not added during intialization on Startup
            if (!EntitiesToInitialize.Contains (addedEntity.Guid)) {
                AddEntityToPersisted (addedEntity);
            } else {
                EntitiesToInitialize.Remove(addedEntity.Guid);
            }
            addedEntity.ChangedAttribute += new EventHandler<ChangedAttributeEventArgs>(OnAttributeChanged);
        }

        /// <summary>
        /// Event Handler for EntityRemoved event fired by the entity registry. Once an entity is removed from the registry,
        /// its entry is also deleted from the database (including cascaded deletion of its component instances)
        /// </summary>
        /// <param name="sender">Sender of the event (the EntityRegistry)</param>
        /// <param name="e">Event Arguments</param>
        internal void OnEntityRemoved(Object sender, EntityEventArgs e) {
            RemoveEntityFromDatabase (e.Entity);
        }

        /// <summary>
        /// Event Handler for AttributeInComponentChanged on Entity. When fired, the value update is queued to be persisted on the next persistence
        /// save to the database
        /// </summary>
        /// <param name="sender">Sender of the event (the Entity)</param>
        /// <param name="e">Event arguments</param>
        internal void OnAttributeChanged(Object sender, ChangedAttributeEventArgs e) {
            //Entity changedEntity = (Entity)sender;
            Guid changedAttributeGuid = e.Component.Definition[e.AttributeName].Guid;
            // TODO: change cascading persistence of entity, but only persist component and take care to persist mapping to entity as well
            AddAttributeToPersisted (changedAttributeGuid, e.NewValue);
        }

        /// <summary>
        /// When a Component was upgraded to a new version, the entity that is informing about the upgrade is entirely (cascaded) persisted to the database
        /// </summary>
        /// <param name="sender">Sender of the event (the Entity)</param>
        /// <param name="e">Event arguments</param>
        internal void OnComponentUpgraded(Object sender, ComponentEventArgs e) {

            // TODO: change cascading persistence of entity, but only persist component and take care to persist mapping to entity as well
            AddEntityToPersisted (e.Component.Parent);
        }
        #endregion

        #region database synchronisation

        /// <summary>
        /// Thread worker function that performs the persisting steps for queued entities and attributes
        /// </summary>
        private void PersistChangedEntities() {
            while (true)
            {
                lock (entityQueueLock)
                {
                    if (EntitiesToPersist.Count > 0 )
                    {
                        CommitCurrentEntityUpdates();
                        EntitiesToPersist.Clear();
                    }
                }
                lock (attributeQueueLock)
                {
                    if (AttributesToPersist.Count > 0)
                    {
                        CommitCurrentAttributeUpdates();
                        AttributesToPersist.Clear();
                    }
                }
                Thread.Sleep(5);
            }
        }

        /// <summary>
        /// Flushes the queue of stored entity updates and commits the changes to the database
        /// </summary>
        private void CommitCurrentEntityUpdates()
        {
            if (GlobalSession.IsOpen)
                GlobalSession.Close();

            using (ISession session = SessionFactory.OpenSession())
            {
                var transaction = session.BeginTransaction();
                foreach (Entity entity in EntitiesToPersist)
                {
                    session.SaveOrUpdate(entity);
                }
                try
                {
                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                }
                finally
                {
                    session.Close();
                }
            }
        }

        /// <summary>
        /// Converts an object to a byte array that can the be persisted as field value for an attribute
        /// </summary>
        /// <param name="obj">The value to be converted</param>
        /// <returns>The byte array representation of the object</returns>
        private byte[] ObjectToByteArray(Object obj)
        {
            if (obj == null)
                return null;
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            bf.Serialize(ms, obj);
            return ms.ToArray();
        }

        /// <summary>
        /// Flushes the queue of stored attribute updates and commits the changes to the database
        /// </summary>
        private void CommitCurrentAttributeUpdates()
        {
            if (GlobalSession.IsOpen)
                GlobalSession.Close();

            using (IStatelessSession session = SessionFactory.OpenStatelessSession())
            {
                var transaction = session.BeginTransaction();
                foreach (KeyValuePair<Guid, object> attributeUpdate in AttributesToPersist)
                {
                    String updateQuery = "update Attribute set value = :newValue where Guid = :entityGuid";

                    IQuery sqlQuery = session.CreateQuery(updateQuery)
                        .SetBinary("newValue", ObjectToByteArray(attributeUpdate.Value))
                        .SetParameter("entityGuid", attributeUpdate.Key);
                    sqlQuery.ExecuteUpdate();
                }
                try
                {
                    transaction.Commit();
                }
                catch (Exception e)
                {
                    logger.WarnException("Failed to update Attribute", e);
                    transaction.Rollback();
                }
                finally
                {
                    session.Close();
                }
            }
        }

        /// <summary>
        /// Adds an enitity update to the list of entities that are queued to be persisted
        /// </summary>
        /// <param name="changedEntity">The entity to be persisted</param>
        private void AddEntityToPersisted(Entity changedEntity) {
            lock (entityQueueLock)
            {
                if (!EntitiesToPersist.Contains(changedEntity))
                    EntitiesToPersist.Add(changedEntity);
            }
        }

        /// <summary>
        /// Adds an attribute update to the list of attribute that are queued to be persisted
        /// </summary>
        /// <param name="changedAttributeGuid">Guid of the Attribute that has changed</param>
        /// <param name="newValue">New value of the changed attribute</param>
        private void AddAttributeToPersisted(Guid changedAttributeGuid, object newValue)
        {
            lock (attributeQueueLock)
            {
                if (!AttributesToPersist.ContainsKey(changedAttributeGuid))
                    AttributesToPersist.Add(changedAttributeGuid, newValue);
                else
                    AttributesToPersist[changedAttributeGuid] = newValue;
            }
        }

        /// <summary>
        /// Removes an entity from database.
        /// </summary>
        /// <param name="entity">Entity.</param>
        private void RemoveEntityFromDatabase(Entity entity) {
            using (ISession session = SessionFactory.OpenSession()) {
                var transaction = session.BeginTransaction ();
                session.Delete (entity);
                transaction.Commit ();
            }
        }

        /// <summary>
        /// Persists the component to database.
        /// </summary>
        /// <param name="component">Component.</param>
        private void PersistComponentToDatabase(Component component) {
            using(ISession session = SessionFactory.OpenSession()) {
                var transaction = session.BeginTransaction ();
                session.SaveOrUpdate (component);
                transaction.Commit ();
            }
        }

        /// <summary>
        /// Retrieves the component registry from database.
        /// </summary>
        internal void RetrieveComponentRegistryFromDatabase()
        {
            ComponentRegistryPersistence persistedRegistry = null;
            using(ISession session = SessionFactory.OpenSession())
                session.Get<ComponentRegistryPersistence>(ComponentRegistryPersistence.Guid);
            if(persistedRegistry != null)
                persistedRegistry.RegisterPersistedComponents ();

        }

        /// <summary>
        /// Retrieves the entities from database.
        /// </summary>
        internal void RetrieveEntitiesFromDatabase()
        {
            IList<Entity> entitiesInDatabase = new List<Entity> ();
            using (ISession session = SessionFactory.OpenSession())
            {
                entitiesInDatabase = session.CreateQuery("from " + typeof(Entity)).List<Entity>();
                foreach (Entity e in entitiesInDatabase)
                {
                    if (e.Parent == null)
                    {
                        EntitiesToInitialize.Add(e.Guid);
                        World.Instance.Add(e);
                    }
                }
            }
        }

        #endregion

        private Configuration NHibernateConfiguration = new Configuration();
        internal ISessionFactory SessionFactory;
        private HashedSet<Guid> EntitiesToInitialize = new HashedSet<Guid>();
        private object entityQueueLock = new object();
        private object attributeQueueLock = new object();
        private List<Entity> EntitiesToPersist = new List<Entity>();
        private Dictionary<Guid, object> AttributesToPersist = new Dictionary<Guid, object>();
        internal ISession GlobalSession;
        internal readonly Guid pluginGuid = new Guid("d51e4394-68cc-4801-82f2-6b2a865b28df");

        private static Logger logger = LogManager.GetCurrentClassLogger();
    }
}
