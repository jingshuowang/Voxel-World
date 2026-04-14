# System Module

Core game logic that runs on the CPU. No OpenGL calls.

## Files
- **Config.java** - Movement, physics, and camera constants
- **Chunk.java** - 16x16x16 chunk data, terrain generation (Perlin noise), greedy meshing
- **VoxelData.java** - Block vertex definitions
- **AABB.java** - Axis-aligned bounding box for collision detection
- **SimplexNoise.java** - Noise generation for terrain

## Responsibilities
- Terrain generation (Perlin noise, biome layers)
- Physics (gravity, collision, movement)
- Chunk management (loading, unloading, block queries)
- World state (block get/set)

# tactiq.io free youtube transcript
# I Rebuilt My Game Engine To Optimise This Properly
# https://www.youtube.com/watch/40JzyaOYJeY

00:00:00.080 have you ever been excited about a new
00:00:01.719 game only to be disappointed that it
00:00:04.879 lags I've spent the past six years
00:00:07.160 creating a game engine and I've been
00:00:08.960 shocked at the things that can make or
00:00:10.759 break performance I'll show four simple
00:00:13.519 optimizations that you can use to make
00:00:15.480 your games run much quicker and instead
00:00:17.720 of just talking about them we'll use
00:00:19.520 them to optimize something that's found
00:00:20.920 in a lot of games voxal voxal are these
00:00:24.000 small cubes and this world is composed
00:00:25.960 of millions of them right now this runs
00:00:28.199 at 50 frames a second and will increase
00:00:30.439 it up to 2500 using the first
00:00:32.960 optimization if we render these voxal
00:00:35.120 one at a time we would render this map
00:00:37.079 at one frame a second that's the
00:00:39.200 opposite of optimization so instead
00:00:41.719 we'll group them into chunks of 32 all
00:00:44.239 these voxal now share the same mesh
00:00:46.360 which lets us render all of them at the
00:00:47.879 same time we can then combine more
00:00:49.960 chunks together to create a larger world
00:00:52.800 this works but it renders very slowly so
00:00:55.359 let's start optimizing it if we have a
00:00:57.520 look at the mesh for a chunk and then
00:00:59.079 zoom in further in into a voxal we can
00:01:01.280 see that it's composed of 12 triangles
00:01:03.800 that means the entire world is composed
00:01:05.560 of 115 million triangles the less
00:01:08.280 triangles we have the faster our game
00:01:10.119 will run so let's get rid of some if we
00:01:12.640 look at these two voxal there's four
00:01:14.479 triangles between them that will never
00:01:16.159 be able to see so when we generate the
00:01:18.439 mesh for this chunk we'll skip the
00:01:20.040 triangles that are between two voils
00:01:22.520 this brings our triangle count down
00:01:24.079 massively to only 1.2 million but we can
00:01:26.720 reduce it further if we look at this
00:01:28.799 wall we can see it's composed of voxel
00:01:30.759 that all have the same metal texture it
00:01:33.159 looks like a large flat surface but it's
00:01:35.200 really composed of 1500 triangles we
00:01:37.880 don't need all these individual
00:01:39.159 triangles we're better off combining
00:01:40.920 them together the algorithm for this is
00:01:43.079 too big to fit on screen but I've put a
00:01:44.960 link to it in the description we can
00:01:46.880 also combine them sideways but not until
00:01:48.960 we've done another optimization in step
00:01:50.759 four this brings us up to 2500 frames
00:01:53.600 per second just by reducing the amount
00:01:55.560 of triangles the next thing we can
00:01:57.560 optimize is what's stored inside each
00:01:59.360 triangle
00:02:00.439 every voxal face has two triangles and
00:02:02.680 each triangle is composed of three
00:02:04.399 vertices each of these vertices stores
00:02:06.759 position and normal vectors and a
00:02:08.360 texture ID this adds up to 28 bytes per
00:02:11.319 vertex and for the entire world that's
00:02:13.319 24 mbes reducing our triangle count
00:02:16.160 increased our performance and so we're
00:02:18.200 reducing our memory usage so we'll
00:02:20.480 compress all this data into one integer
00:02:22.920 without losing quality to start every
00:02:25.560 vertex needs a position currently we're
00:02:27.640 using a vector for this which holds
00:02:29.319 three FL
00:02:30.680 these floats are great for standard 3D
00:02:32.400 models because they can store fractional
00:02:34.280 values but every triangle in our mesh
00:02:36.480 lines up to a grid so we can use a bite
00:02:38.720 instead that's a quarter the size of a
00:02:40.920 float but we can go even smaller a bite
00:02:43.879 holds eight bits and can store numbers
00:02:45.760 up to 255 but our chunks are only 32
00:02:49.200 voxal wide with seven bits our mesh will
00:02:52.000 be half the size at up to 127 voxal wide
00:02:55.680 with six bits our mesh can be up to 63
00:02:57.840 voxal wide and with 5 bits our mesh can
00:03:00.239 be up to 31 voxal wide but our chunk
00:03:02.480 can't fit into that so we'll use six
00:03:04.599 bits for each of the X Y and Zed
00:03:06.879 positions we can reduce this further but
00:03:09.159 not until we've done another
00:03:10.200 optimization in step three each vertex
00:03:13.239 also stores a normal Vector which helps
00:03:15.280 us calculate lighting and other effects
00:03:17.760 this Vector stores the direction that
00:03:19.200 each triangle is facing for example here
00:03:21.879 all the green triangles are facing up
00:03:23.920 and all the red triangles are facing
00:03:25.519 left since boxes are cubes there's only
00:03:28.040 six possible normals they can have so
00:03:30.360 we'll store the number zero for up one
00:03:32.360 for down two for right and so on then in
00:03:35.400 the vertex Shader we can turn this
00:03:37.120 number back into a vector we can fit
00:03:39.519 these six directions inside three bits
00:03:41.560 which is a huge saving lastly each
00:03:43.959 triangle contains a texture ID which
00:03:46.080 tells the Shader which texture to apply
00:03:48.640 we only have 70 unique textures so we
00:03:50.959 can store this ID using seven bits all
00:03:53.879 the data we need to render a Vertex can
00:03:55.640 now fit inside 32 bits which is the size
00:03:58.040 of one of the floats from the old vector
00:04:00.599 but the vertex Shader needs to know how
00:04:02.079 to unpack this data into its separate
00:04:04.120 components we'll use bit masking for
00:04:06.439 this the top row here is a mask and
00:04:08.680 matches the number after the N symbol in
00:04:10.720 the Shader when we apply this mask to
00:04:12.920 our vertex data we get a new number with
00:04:15.200 only the bits that line up with the mask
00:04:17.600 to unpack the rest of our data we'll
00:04:19.519 shift it to the right and then apply the
00:04:21.279 mask again we now have all our data and
00:04:23.960 can run our Shader as usual but all the
00:04:26.400 chunks are overlapped in the same
00:04:27.960 position that's because every vertices
00:04:30.240 position is now in the range 0 to 32 so
00:04:33.440 we need to tell each chunk where it is
00:04:35.360 in the world to do this we'll create a
00:04:37.680 world position uniform in the vertex
00:04:39.280 Shader and update it before drawing each
00:04:41.639 chunk the vertex Shader then adds this
00:04:43.919 world position to the vertices position
00:04:46.600 our world now renders correctly and our
00:04:48.520 memory usage is down to 3.4 mbes this
00:04:51.759 increased our frames a second up to
00:04:53.199 4,000 just by reducing the memory usage
00:04:56.520 but we're still storing more data than
00:04:58.000 we need to if we look at this wireframe
00:05:00.720 every voxal face has six vertices and
00:05:03.199 two of them have the exact same position
00:05:05.520 storing the same data twice feels like a
00:05:07.440 waste can we render a voxal face with
00:05:09.600 only four vertices instead currently
00:05:12.440 these triangles are rendered
00:05:13.520 individually which means we need three
00:05:15.360 vertices for each of them but open G
00:05:18.000 also lets us render triangles as strips
00:05:20.319 where each extra vertex builds a new
00:05:22.080 triangle off the last the problem is
00:05:24.880 everything gets joined together as one
00:05:26.720 long strp I thought surely there's a way
00:05:29.120 to tell open to start a new strip every
00:05:31.600 four vertices but I couldn't find
00:05:33.759 anything but I found an even better
00:05:35.880 solution when games render particles
00:05:38.160 they use instancing they have a base
00:05:40.440 particle model and a list of positions
00:05:42.960 and then open gel draws this model at
00:05:44.840 all of these positions we didn't have to
00:05:47.120 create a huge mesh with all these
00:05:48.720 particles in it and even better each
00:05:51.199 particle is a triangle strip and they're
00:05:52.880 not joined together so we'll use
00:05:54.919 instancing to render our voxels we'll
00:05:57.199 have a base model for the voxal face
00:05:59.039 which has only four vertices and then
00:06:01.319 instead of a list of positions we have a
00:06:03.199 list of our 32-bit voxal data this voxal
00:06:06.240 data contains everything we Ed to store
00:06:08.039 in the mesh but rather than storing it
00:06:10.080 six times for each face we only have to
00:06:12.160 store it once but when we render the
00:06:14.319 world everything is facing up that's
00:06:16.720 because our base voxal model is facing
00:06:18.639 up we need to rotate it based on the
00:06:20.560 face Direction in the vertex Shader if
00:06:23.080 the face is up we'll lift the model up
00:06:25.639 if it's facing left we'll rotate it to
00:06:27.440 the side and the same for the other
00:06:29.199 direction
00:06:30.840 but if we look at the world we still
00:06:32.440 have all these gaps that's because
00:06:34.440 earlier we combined our triangles
00:06:36.120 together and we're no longer storing how
00:06:38.039 long each combined face is we need to
00:06:40.400 store this length but we don't have room
00:06:42.280 for another six bits thankfully now that
00:06:44.720 we're using instancing we can reduce the
00:06:46.840 size of our position Data before we had
00:06:49.080 to store positions in the range 0 to 32
00:06:51.639 which is 33 unique numbers but since our
00:06:54.400 base voxal model is already one unit
00:06:56.400 wide we only need to store positions up
00:06:58.360 to 31 five bits can store exactly 32
00:07:01.639 unique numbers so we're in luck we also
00:07:04.599 only need five bits to store the length
00:07:06.599 because each combined face can be up to
00:07:08.280 32 voxal long now in the vertex Shader
00:07:11.319 we can use this length value to stretch
00:07:13.199 out the base voxal
00:07:14.960 model this reduced our memory usage by a
00:07:17.479 factor of six down to 580 Koby that's
00:07:21.319 because we're only storing one vertex
00:07:22.840 for each base instead of six also the
00:07:25.879 vertex Shader only runs four times for
00:07:27.879 each face because we're using triangle
00:07:30.120 strips together this increased our
00:07:32.240 frames a second up to 6500 and the next
00:07:35.280 optimization will increase it even
00:07:37.080 further to render this world we're
00:07:39.199 sending all these commands to the
00:07:40.759 graphics card this is typical for a game
00:07:43.280 but we can replace them all with just
00:07:44.800 one to do this we need to change the way
00:07:47.280 we store our data right now each chunk
00:07:49.879 has its own list of instance data in its
00:07:51.800 own buffer this means we need to ask the
00:07:54.039 graphics card to render each of them one
00:07:56.080 at a time instead we can create one huge
00:07:58.720 buffer and give each chunk a small
00:08:00.599 portion of it to render a specific part
00:08:02.879 of this buffer we can use the longest
00:08:04.560 open gel function ever created the first
00:08:07.159 two numbers refer to the four vertices
00:08:09.159 in our base Vox or model the next two
00:08:11.560 numbers refer to the start and length of
00:08:13.639 the part of the massive buffer that we
00:08:15.039 want to draw if we want to render
00:08:17.039 multiple parts of this buffer we could
00:08:18.879 repeat this function but now we're back
00:08:21.000 to multiple draw commands we could just
00:08:23.319 render the entire combined buffer but
00:08:25.319 that would render chunks that are
00:08:26.520 outside our field of view we only want
00:08:28.599 to render chunks that we can actually
00:08:30.159 see so what we'll do instead is store
00:08:32.640 these parameters in a special kind of
00:08:34.279 buffer called an indirect buffer then we
00:08:36.719 can use a second longest open gel
00:08:38.360 function to render them all at the same
00:08:40.360 time but when we draw them we have that
00:08:42.719 issue from before where they're all
00:08:44.320 overlapped that's because we can't use
00:08:46.279 Shader uniforms anymore to position each
00:08:48.440 chunk to fix this we'll use another
00:08:50.720 buffer called a Shader storage buffer
00:08:52.680 object we can store anything we want in
00:08:54.680 this buffer and in this case we'll store
00:08:56.560 the weld position of every chunk we're
00:08:58.360 trying to draw each of these commands
00:09:00.560 has a unique ID called GL draw ID this
00:09:03.680 ID increments for each of our draw
00:09:05.399 commands so we can use it to select the
00:09:07.279 right position for each chunk we're now
00:09:09.560 rendering the entire world with one draw
00:09:11.640 command which increased our frames a
00:09:13.480 second up to
00:09:14.760 12,000 that's because our CPU spends
00:09:17.200 hardly any time telling the graphics
00:09:18.680 card what to do and the graphics card
00:09:20.519 has a huge command it can focus on but
00:09:22.920 12,000 frames a second isn't the limit
00:09:25.399 now that we're using one draw command
00:09:27.160 we've unlocked two bonus optimizations
00:09:29.959 the first is related to an optimization
00:09:31.880 that every game uses to ignore triangles
00:09:34.160 that are on the other side of a model it
00:09:36.360 works by checking if the points in the
00:09:37.920 Triangle are going clockwise and if they
00:09:40.240 aren't the triangle isn't rendered this
00:09:42.519 speeds up rendering because we're not
00:09:44.040 wasting time on the triangles that are
00:09:45.640 facing away from us but the vertex
00:09:47.680 Shader still has to run every frame to
00:09:49.800 determine if these triangles are
00:09:51.160 clockwise or not if we only render the
00:09:53.399 anticlockwise triangles we can see there
00:09:55.440 are a lot of them that are facing away
00:09:56.920 from us this means we're running the
00:09:58.760 vertex shade for all these triangles and
00:10:01.040 then just discarding them but we can
00:10:03.079 stop these triangles from ever reaching
00:10:04.800 the vertex Shader let's say the play is
00:10:07.079 standing in this blue Chunk we know
00:10:09.160 they'll never be able to see the red
00:10:10.440 triangles in these chunks because
00:10:12.200 they're facing away from them we also
00:10:14.560 know they'll never be able to see these
00:10:16.079 triangles in these chunks so rather than
00:10:18.920 creating one mesh for each chunk we'll
00:10:21.000 create six the first mesh only contains
00:10:23.800 triangles that are facing up the second
00:10:26.200 only contains triangles that are facing
00:10:27.920 down and so on we're still only using
00:10:30.560 one buffer and one draw command but we
00:10:32.839 have an extra step to only render the
00:10:34.560 meshes that are facing the player the
00:10:36.760 vertex Shader is no longer wasting time
00:10:38.480 on triangles the player can't see which
00:10:40.600 brings our frames a second up to 14,000
00:10:43.440 we're now up to the final optimization
00:10:45.959 earlier I mentioned that we can only
00:10:47.440 combine faces in One Direction not two
00:10:50.480 that's because we don't have room to
00:10:51.800 store another five bits for the combined
00:10:53.720 horizontal length but we can make room
00:10:56.120 by deleting the face direction we can do
00:10:58.600 this because of the Shader storage
00:10:59.959 buffer object we created before right
00:11:02.480 now it stores the world position of each
00:11:04.240 mesh in each chunk but we can also store
00:11:06.639 the face direction of each mesh this
00:11:09.079 works because now each chunk has a
00:11:10.800 separate mesh for each face Direction so
00:11:13.160 in the vertex Shader we'll replace our
00:11:15.079 face code with the
00:11:16.839 ssbo we now have 5 bits free in our
00:11:19.279 vertex data which is enough to store the
00:11:21.399 second combined face length by combining
00:11:24.040 faces in two directions we've reduced
00:11:25.920 our triangle count down to
00:11:27.800 79,000 this is now running at 177,000
00:11:30.560 frames per second which is the highest
00:11:32.320 it'll go compared to the start we're
00:11:34.639 using a tiny fraction of our original
00:11:36.440 memory usage and triangle count this
00:11:38.880 reduction is the main reason it's
00:11:40.160 rendering so much faster you can run
00:11:42.519 these demos yourself and experiment with
00:11:44.160 the code details are in the video
00:11:46.240 description there are three more
00:11:47.959 optimizations that I use to render
00:11:49.800 massive amounts of terrain which you can
00:11:51.560 watch in this video on
00:11:56.000 screen
basically for rendering firstly, render using meshes so for each chunk they all render at the same time each chunk is a 16 time 16 times 16 cube. so we can eleiminate triangles to get rid to increase speed. for empty spaces we call it air blocks and we dont render them. butr this is very usefgul for things like ledge crouching and also what we are doing now so only render faces that is touching BOTH air and solid blocks this makes faces not render in the air but also not in the ground. making stuf go less,also we use greedy meshes which is in the guide folder, this eliminates triangles by combining samefacing ajacent and triangles that can make a giant treiangle for less rnedering. Next step is to make less data for each triangl (square) to optimise the data storage. Firstly we dont need ot use vec3 since all the faces in the game line up to the 3d grid so we can accualy store the data of the triangles corner as a byte, for each chunk we only need 6 bits. currently the data is zzzzzzyyyyyyxxxxxx. Step 3, each side also stores a normal vector which handles a lot of stuf of lighting and effects, it stored the direction its facing, the normal. theres only 6 possible normals 0 for y+ 1 for y- 2 for x+ 3 for x- 4 for z+ 5 for z-.  so now its fffzzzzzzyyyyyyxxxxxx. we also need a texutre for each one so lets add texutres which is just green for now so just 1 bits enough but the video shows 7 so why not , now its tttttttfffzzzzzzyyyyyyxxxxxx. this cna be decoded using bit masking for decoding pos x y z the direction and texture ID. which is simple to make a simple image selector for a png 0 for top lefdt and goes right until last one in first row and then next rows most left onee and so on until bottom right. each image is 16 by 16 pixels in texture.png. ok now we have a problem all the chunks stack on top each other to fix this each chunk has a world position multiplied by a factor which is added to the triangles(squares) position or you could say t he verticies position now we have a decent fps 2000 but not good enough. step 3 is to use opengls particle system, this is good since it uses instancing, you can set the base model as a face of a block, a square this has 4 verticies. and this has data which is the one we tlaked about earlier 0000tttttttfffzzzzzzyyyyyyxxxxxx, instead of drawing 6 times you only need to draw once.  bu8t everything is facing up oh no, now we need to add face direction which is already stored in data so we can use that to rotate the model t the correct direction. but since we are using the base model we only need 31 at max(this is wrong sine we using chunks of 16 but the video make it perfectly fit into a byteso we going to say we have 31 at most) this means we can simplify to tttttttfffzzzzzyyyyyxxxxx. we also need 5 units for length in greedy meshing so now its 00llllltttttttfffzzzzzyyyyyxxxxx. so the model is only effected by the texture, face(rotate) and length(witdh we adding later) which is just stretching the modle. step 4: right now each chunk has its own data which is stoobid, we can fuse all of the chunks data into one large buffer, to render at one time. We can render this with a function which only has a import of the base modle and 4 numebrs the first 2 refers to the base voxel model and the next two is the start and length of the part of the buffer we using(ex glDrawArraysInstancedBaseInstance(Gl_triangle_strip, 0,4,15,16) thich means to take value from the buffur form #15 to #30)we could repeat the function but thats multiple draw calls instead we goiing to use a indirect buffer and then opengl will render them all at the same time but it overlaps since we cant use shader uniforms, to fix this we can create a ssbo, shader storage buffer object, we can store anything we want, we will store the world position of every chunk we are TRYING to draw dont waste time on ones we arent drawing. Lastly some tricks can make ur fps peaking at 20000, firstly we can ignore triangles facing away, for this to work remeber the combined buffur instead of each chunk being a whole mesh we can create 6 meshes one for every direction, same order y+ y- x+ x- z+ z-, we are still ony lahving one draw but onyl rendering the mesh facing the player. and remeber in stpe 3 we said we gona add more width yessir we can delete the face direction in data straip since simemeber the ssbo, ssbo+inderect buffer+thedrawid draws a mesh or block im not sure but its obvious. we can accualy store the direction of each mesh to the ssbo making it a vec4. now we have perfectly 5 more bits in our vertex data so its now lllllwwwwwtttttttfffzzzzzyyyyyxxxxx(in greeedy meshing w which is witdh just makign it stretchs the other way its technally wit ha length dont overcomplicate it has nothing to do with 3d). Basically render smart and make data use bytes instead of vec 3 and 4 if can and other parts are explaiend before.
STOP HERE
after these rendering trick theres this gmaeplan firstly dont complicate to to fit these rnedering strat we will have to simliplity a bit a add variatiosn later for now we will just use one block type and one texture(just solid dark green). so each chunk is 16 times 16 times 16 blocks. fore the systme part there is a 3d grid and each block is a cube with y going up and x and z going horizontally. player movement is kept the same and it hanbdkles world generation and physics etc. basically all stuf rendering dosent do perlin noise with high prequency and low amplitude and then make the hights perlin nosie to power of 2. For rendering part  its simple combine tricks with simple rendering(just render each block as a dark green cube for now) which includes block rendering renderign the sky which is just blue for now with simle saylight cycle the path of the sun and moon is on a circle them 2 oposite to each other. the sky just turns darker blue at night. camera is handled by both system and rendering so put camera stuf in rendering and system with system doing cpu stuf and rendering doing gpu stuf. the rendering auso includes audio rendering. Handle shaders later. Also keep variable names simple greedy meshiong in guide folder

second guide video, volumetric light, because classic methods of 3d rendering is way too costly I have ummerized better methods, we choose random positions and draw flat squares there all of them facing the same direction and stretched up in the y axis and its a rectangle. add a sun colored gradient to the rectangle and lastly angle these planes towards the sun. The fectrangle is easily rendered as 2 triangles. but theres a problem the light isnt rendered in corret spots since its random, we can fix this by cheaking samples aroudn the light and the light is brightest when theres 
over half of them in sunlight the beam is brightest(opacity = 1-abs(percentSunlight)*2). we can use the transform feedback buffer to average each ray of lights opacity to make it flicker less. vertex sahder gives to transform readback buffer then trb gives to cpu then cpu cauculates average gives to ssbo and lastly goes back to vertex shaders.

3rd guide: ambient occlusion, kinda simple kinda compl;ex, defered shading, for each pixel if the sample point is futher away to the camera than the other sampler points we consider that to be blocking ambient light. but this is bad a shpere sahped area will fial on a floor and too costly. we can further imporve this by move the sampler point away form the noirmal slightly for better reselts. this is ok but we can use baked ambient oclusion, the blocks don't move so this is ok for now, for example if the blocks that are ajcent to a edge are 90 degrees from each other, parts close to the edge will we sahdered darker idk how t o explain but you get it.

More Techniques: Frustum culling, this is based on normal since normals shows direction of faces, this seems complex but just dont render the faces that is facing away, LOD, very simple 
lod, basiacally for every few layers of chunks getting bigger we make texture lower and block count lower and ocmbine the chunks / meshes. sinking isnt avalible since we create complex system, so rendering increases as distance increases. so we should have back face culling, frustum culling, occlusion culling is jsut not rendering things behidn another, and level detail. And greedy meshing. Then use multithreadting. to separate chung genration like rendering and accual system code(generation, system etc).

Servers: this is the most easiest, its not fancy it just uses seeds and make player stuf sychronize player position. new player made when new username/passord, the ip is the seed and they join and idk if we need ports. player just a prism, yes just a rectangular prim 2 meter tall. theres a secret the server only save changes so it dosent crash, the same world seed makes the same world every time.
future adtitives after im done learning them:w
Complex sahdoers, shadows, realistic fluids, gloom, fog, reflection, PBR(likely not), raycast(probably not too lmao)temporary antiailias, gamma correction, motion blur, refined voxel, physcial . Combatt, bultiplyaer, biome etc.

Aliasing, just use anti aliasing.

Terrion noise example:
vec4 GetChannel0(vec2 uv)
{
    uv = clamp(uv, vec2(0.001), vec2(0.999));
    uv *= BUFFER_SIZE / iResolution.xy;
    return texture(iChannel0, uv);
}

vec4 GetChannel1(vec2 uv)
{
    uv = fract(uv);
    uv *= BUFFER_SIZE / iResolution.xy;
    return texture(iChannel1, uv);
}

vec4 map(vec3 p, out float erosion)
{
    vec2 uv = p.xz + vec2(0.5) - vec2(0.5) / BUFFER_SIZE;
    
    vec4 tex = GetChannel0(uv);
    float height = tex.x;
    vec3 normal = tex.yzz;
    normal.y = sqrt(1.0 - dot(normal.xz, normal.xz)); // Recover Y
    
    erosion = tex.w;
    
    return vec4(height, normal);
}

// Ray marching
float march(vec3 ro, vec3 rd, out vec3 normal, out int material, out float s_t)
{
    s_t = 9999.0;
    
    vec3 boxNormal;
    vec2 box = boxIntersection(ro, rd, vec3(0.5, 1.0, 0.5), boxNormal);
    
    if (box.y < 0.0)
    {
        return -1.0;
    }
    
    float tStart = max(0.0, box.x) + 1e-2;
    float tEnd = box.y - 1e-2;
    
    material = M_GROUND;

    float stepSize = 0.0;
    float stepScale = 1.0;
    float t = tStart;
    float altitude = 0.0;
    for (int i = 0; i < 32; i++)
    {
        vec3 pos = ro + rd * t;
        
        float foo;
        vec4 tex = map(pos, foo);
        float h = tex.x;
        normal = tex.yzw;
        
        altitude = pos.y - h;
        
        s_t = max(0.0, min(s_t, altitude / t));
        
        if (altitude < 0.0)
        {
            if (i < 1) // Sides
            {
                /*
                if (diff < -0.1) // Bottom
                {
                    return -1.0;
                }
                */
                if (pos.y < 0.35) // Flat bottom
                {
                    s_t = 9999.0;
                    return -1.0;
                }
                normal = boxNormal;
                material = M_STRATA;
                break;
            }
        }
        
        if (altitude < 0.0)
        {
            // Step back (contact/edge refinement)
            stepScale *= 0.5;
            t -= stepSize * stepScale;
        }
        else
        {
            // Step forward
            // Accelerate the ray by distance to terrain. This would result in horrible aliasing if we didn't do refinement above
            stepSize = abs(altitude) + 1e-2;
            //stepSize = (tEnd - tStart) / float(stepCount);
            t += stepSize * stepScale;
        }
    }
    
    if (t > tEnd)
    {
        s_t = 9999.0;
        return -1.0;
    }
    
#ifdef WATER
    vec3 waterNormal;
    vec2 water = boxIntersection(ro, rd, vec3(0.5, WATER_HEIGHT, 0.5), waterNormal);
    if ((water.y > 0.0 && (water.x < t || t < 0.0)) && material != M_STRATA)
    {
        t = max(0.0, water.x);
        normal = waterNormal;
        material = M_WATER;
    }
    else
#endif

    if (box.y < 0.0)
    {
        return -1.0;
    }

    return t;
}

vec3 GetReflection(vec3 p, vec3 r, vec3 sun, float smoothness)
{
    vec3 refl = SkyColor(r, sun) * 4.0;
    
    vec3 foo;
    float r_t;
    int r_material;
    march(p, r, foo, r_material, r_t);
    return refl * (1.0 - exp(-r_t * 10.0 * sq(smoothness)));
}

// Main image output
void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
#ifdef SHOW_BUFFER
    float debugWidth = iResolution.y / 2.0;
#else
    float debugWidth = 0.0;
#endif

    // ==========================================================================================
    // Set up camera
    // ==========================================================================================
    vec2 cameraAngle = vec2(iTime * 0.1 + PI * 1.5, -0.17 * PI);
    float cameraDistance = 5.0;
    
    // Intro animation
    cameraAngle.x -= exp(-iTime * 5.0) * 4.0;
    cameraDistance += exp(-iTime * 5.0) * 5.0;
        
    if (iMouse.z > 0.5)
    {
        cameraAngle.x = PI / 2.0;
        cameraAngle.y = -0.2 * PI;
    }
    
    vec3 ro = vec3(0.0, 0.325, 0.0);
    vec3 rd = CameraRay(11.0, iResolution.xy, fragCoord.xy - vec2(debugWidth / 2.0, 0.0));
    
    mat3 rot = CameraRotation(cameraAngle.yx);
    rd = rot * rd;
    ro = rot * ro + rot * vec3(0, 0, cameraDistance);
    
    // ==========================================================================================
    // Ray march
    // ==========================================================================================
    vec4 foo;
    vec3 normal;
    int material;
    float t = march(ro, rd, normal, material, foo.w);
    
    // ==========================================================================================
    // Shade
    // ==========================================================================================
#ifdef FIXED_SUN
    vec3 sun = normalize(vec3(-1.0, 0.4, 0.05));
#else
    vec3 sun = rot * normalize(vec3(-1.0, 0.1, 0.25));
#endif
    
    vec3 fogColor = 1.0 - exp(-SkyColor(rd, sun) * 2.0);
    
    vec3 color;
    
    if (t < 0.0)
    {
        // Sky
        color = fogColor * (1.0 + pow(fragCoord.y / iResolution.y, 3.0) * 3.0) * 0.5;
#ifdef SHOW_NORMALS
        color = vec3(0.5, 0.5, 1.0);
#endif
    }
    else
    {
        vec3 pos = ro + rd * t;
        
        float erosion;
        //float h = map(pos, erosion).x;
        float diff = pos.y - map(pos, erosion).x;
        
        vec4 breakupTex = vec4(0.0);
        
#ifdef DETAIL_TEXTURE
        breakupTex = GetChannel1(pos.xz + 0.5);
        vec3 breakupNormal = breakupTex.zyw;
        if (material != M_STRATA)
        {
            normal = normalize(normal + breakupNormal.xzy * 0.1);
        }            
#endif
        float breakup = breakupTex.x;

        vec3 f0 = vec3(0.04);
        float smoothness = 0.0;
        float reflAmount = 0.0;
        float occlusion = 1.0;
        
        vec3 r = reflect(rd, normal);
        
        vec3 diffuseColor = vec3(0.5);
        if (material == M_GROUND)
        {
#ifndef GREYSCALE
            occlusion = sq(saturate(erosion + 0.5));
            
            // Cliffs / Dirt
            diffuseColor = CLIFF_COLOR * smoothstep(0.4, 0.52, pos.y);
            //diffuseColor = mix(diffuseColor, DIRT_COLOR, smoothstep(0.8, 0.9, normal.y - erosion * 0.1 + breakup * 0.05));
            diffuseColor = mix(diffuseColor, DIRT_COLOR, smoothstep(0.3, 0.0, occlusion + breakup * 1.0));
            
            // Grass
            vec3 grassMix = mix(GRASS_COLOR1, GRASS_COLOR2, smoothstep(0.4, 0.6, pos.y - erosion * 0.05 + breakup * 0.3));
            //diffuseColor = mix(diffuseColor, grassMix, smoothstep(0.8, 0.95, normal.y - erosion * 0.2 + breakup * 0.1));
            diffuseColor = mix(diffuseColor, grassMix, smoothstep(WATER_HEIGHT + 0.05, WATER_HEIGHT + 0.02, pos.y - breakup * 0.02) * smoothstep(0.8, 1.0, normal.y + breakup * 0.1));
            
            // Snow
            diffuseColor = mix(diffuseColor, vec3(1.0), smoothstep(0.53, 0.6, pos.y + breakup * 0.1));
    #ifdef WATER
            // Sand (beach)
            diffuseColor = mix(diffuseColor, SAND_COLOR, smoothstep(WATER_HEIGHT + 0.005, WATER_HEIGHT, pos.y + breakup * 0.01));
    #endif
            diffuseColor *= 1.0 + breakup * 0.5;
#endif
        }
        else if (material == M_STRATA)
        {
#ifndef GREYSCALE
            vec3 strata = smoothstep(0.0, 1.0, cos(diff * vec3(130.0, 190.0, 250.0)));
            diffuseColor = vec3(0.3);
            diffuseColor = mix(diffuseColor, vec3(0.50), strata.x);
            diffuseColor = mix(diffuseColor, vec3(0.55), strata.y);
            diffuseColor = mix(diffuseColor, vec3(0.60), strata.z);
            
            diffuseColor *= exp(diff * 10.0) * vec3(1.0, 0.9, 0.7);
#endif
        }
        else if (material == M_WATER)
        {
            float shore = normal.y > 1e-2 ? exp(-diff * 60.0) : 0.0;
            float foam = normal.y > 1e-2 ? smoothstep(0.005, 0.0, diff + breakup * 0.005) : 0.0;
        
            diffuseColor = mix(WATER_COLOR, WATER_SHORE_COLOR, shore);
            
            diffuseColor = mix(diffuseColor, vec3(1.0), foam);
            
            //f0 = vec3(0.2);
            smoothness = 0.95;
        }
        
        float shadow = 1.0;
        
#ifdef SHADOWS
        if (material != M_STRATA)
        {
            // Shadow ray
            float s_t;
            int s_material;
            march(pos + vec3(0.0, 1.0, 0.0) * 1e-4, sun, foo.xyz, s_material, s_t);
            shadow = 1.0 - exp(-s_t * 20.0);
            //fragColor = vec4(shadow, shadow, shadow, 1.0);
            //return;
        }
#endif

        // Ambient
        color = diffuseColor * SkyColor(normal, sun) * occlusion * Fd_Lambert();
        // Direct
        color += Shade(diffuseColor, f0, smoothness, normal, -rd, sun, SUN_COLOR * shadow);
        // Bounce
        color += diffuseColor * SUN_COLOR * (dot(normal, sun * vec3(1.0,-1.0, 1.0)) * 0.5 + 0.5) * Fd_Lambert() / PI;
        // Reflection
        color += GetReflection(pos, r, sun, smoothness) * F_Schlick(f0, dot(-rd, normal));
        // Fog
        float fog = exp(-t * t * smoothstep(WATER_HEIGHT, WATER_HEIGHT - 0.5, pos.y) * 0.5);
        //color = mix(fogColor, color, fog);
        
        //color = vec3(exp(-max(0.0, -erosion * 2.0)));
        //color = vec3(occlusion);

#ifdef SHOW_DIFFUSE
        color = pow(diffuseColor, vec3(1.0 / 2.2));
#elif defined(SHOW_NORMALS)
        color = normal.xzy * 0.5 + 0.5;
#endif
    }
    
    vec3 boxNormal;
    vec2 box = boxIntersection(ro, rd, vec3(0.5, 1.0, 0.5), boxNormal);
    
    float costh = dot(rd, sun);
    float phaseR = PhaseRayleigh(costh);
    float phaseM = PhaseMie(costh, 0.6);
    
    vec2 od = vec2(0.0);
    vec3 tsm;
    vec3 sct = vec3(0.0);
    float rayLength = (t > 0.0 ? t : box.y) - box.x;
    float stepSize = rayLength / 16.0;
    for (float i = 0.0; i < 16.0; i++)
    {
        vec3 p = ro + rd * (box.x + (i + 0.5) * stepSize);
        
        float h = max(0.0, p.y - 0.35);
        //float d = exp(-h * 5.0);
        float d = 1.0 - saturate(h / 0.2);
        
        if (p.y < 0.35)
        {
            d = 0.0;
        }
        
        float densityR = d * 1e5;
        float densityM = d * 1e5;
        
        od += stepSize * vec2(densityR, densityM);
        
        tsm = exp(-(od.x * C_RAYLEIGH + od.y * C_MIE));
        
        sct += tsm * C_RAYLEIGH * phaseR * densityR * stepSize;
        sct += tsm * C_MIE * phaseM * densityM * stepSize;
    }
    
    color = color * tsm + sct * 10.0;

#if !defined(SHOW_NORMALS) && !defined(SHOW_DIFFUSE)
    color = Tonemap_ACES(color);
    color = pow(color, vec3(1.0 / 2.2));
#endif

    // Dither
    color += texture(iChannel2, mod(fragCoord.xy, iChannelResolution[2].xy) / iChannelResolution[2].xy).xxx / 255.0;

#ifdef SHOW_BUFFER
    vec2 debugUV = fragCoord / debugWidth;
    if (debugUV.x > 0.0 && debugUV.x < 1.0 && debugUV.y > 0.0 && debugUV.y < 2.0)
    {
        vec4 tex = GetChannel0(fract(debugUV));
        vec3 normal = tex.yzz;
        normal.y = sqrt(1.0 - dot(normal.xz,normal.xz));
        color = debugUV.y > 1.0 ? normal.xzy * 0.5 + 0.5 : tex.xxx * 2.5 - 0.75;
    }
#endif
    
    fragColor = vec4(color, 1.0);
} 
