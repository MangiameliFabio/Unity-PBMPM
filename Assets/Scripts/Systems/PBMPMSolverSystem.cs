using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

partial struct PBMPMSolverSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ScheduleParticleJobs(state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    [BurstCompile]
    private void ScheduleParticleJobs(ref SystemState state)
    {
        var job = new ParticleToGridJob
        {
            GridLookup = state.GetComponentLookup<GridComponent>(true)
        };
        job.ScheduleParallel();
    }

    private void IntegrateParticles()
    {
        
    }
    
    [BurstCompile]
    private partial struct SolveConstraintsJob : IJobEntity
    {
        private void Execute(ref ParticleComponent particle)
        {
            
        }
    }
    
    [BurstCompile]
    private partial struct ParticleToGridJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
        private void Execute(ref ParticleComponent particle)
        {
            GridComponent grid = default;
            bool gridFound = false;
            if (particle.GridCache != null)
            {
                if (GridUtilities.CheckIfInsideBounds(GridLookup[particle.GridCache], particle.Position))
                {
                    grid = GridLookup[particle.GridCache];
                    gridFound = true;
                }
            }

            if (gridFound)
            {
                
            }
            else
            {
                for (int i = 0; i < GridLookup; i++)
                {
                    
                }
            }
        }
    }
    
    [BurstCompile]
    private partial struct GridUpdateJob : IJobFor
    {
        public void Execute(int index)
        {
        
        }
    }
    
    [BurstCompile]
    private partial struct GridToParticleJob : IJobEntity
    {
        private void Execute(ref ParticleComponent shape)
        {
        
        }
    }
}
