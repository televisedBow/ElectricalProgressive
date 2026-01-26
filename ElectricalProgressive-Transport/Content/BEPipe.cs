﻿using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveTransport
{
    public class BEPipe : BlockEntity
    {
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected PipeNetworkManager networkManager;
        
        public bool[] ConnectedSides => connectedSides;
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            // Регистрируем трубу в сети
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);
            
            UpdateConnections();
        }
        
        public virtual void UpdateConnections()
        {
            for (int i = 0; i < 6; i++)
            {
                connectedSides[i] = false;
                connectedPipes[i] = null;
            }
            
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
                
                Block neighborBlock = Api?.World.BlockAccessor.GetBlock(checkPos);
                
                if (neighborBlock is BlockPipeBase)
                {
                    connectedSides[i] = true;
                    connectedPipes[i] = checkPos.Copy();
                    
                    // Обновляем соединение у соседа
                    if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEPipe neighborPipe)
                    {
                        neighborPipe.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEInsertionPipe neighborInserter)
                    {
                        neighborInserter.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                }
            }
            
            MarkDirty();
        }
        
        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos)
        {
            int index = side.Index;
            connectedSides[index] = true;
            connectedPipes[index] = fromPos.Copy();
            MarkDirty();
        }
        
        public void GetBlockInfo(StringBuilder sb)
        {
            int connections = 0;
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i]) connections++;
            }
            
            sb.AppendLine(Lang.Get("vspipefiltermod:connections", connections));
            
            if (networkManager != null)
            {
                var network = networkManager.GetNetwork(Pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("vspipefiltermod:network-size", network.Pipes.Count));
                    sb.AppendLine(Lang.Get("vspipefiltermod:inserters", network.Inserters.Count));
                }
            }
        }
        
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }
        
        public override void OnBlockRemoved()
        {
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedPipes[i] != null)
                {
                    if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEPipe neighborPipe)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborPipe.BreakConnection(facing.Opposite);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEInsertionPipe neighborInserter)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborInserter.BreakConnection(facing.Opposite);
                    }
                }
            }
            
            // Удаляем трубу из сети
            networkManager?.RemovePipe(Pos);
            
            base.OnBlockRemoved();
        }
        
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            MarkDirty();
        }
        
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            
            byte[] connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedSides[i] = connBytes[i] == 1;
                }
            }
        }
        
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            
            byte[] connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                connBytes[i] = (byte)(connectedSides[i] ? 1 : 0);
            }
            tree.SetBytes("connections", connBytes);
        }
    }
}